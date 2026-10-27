using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
// Demostración local sin autenticación: no acepta destinos remotos por configuración.
builder.WebHost.UseUrls("http://127.0.0.1:5088");
builder.Services.AddSingleton<Workflow>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
app.MapGet("/api/state", (Workflow workflow) => workflow.Snapshot());
app.MapPost("/api/proposals", (Proposal request, Workflow workflow) =>
{
    try { return Results.Ok(workflow.Propose(request)); }
    catch (RuleException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapPost("/api/proposals/{id:guid}/approve", (Guid id, Workflow workflow) =>
{
    try { return Results.Ok(workflow.Approve(id)); }
    catch (RuleException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.MapPost("/api/proposals/{id:guid}/reject", (Guid id, Workflow workflow) =>
{
    try { return Results.Ok(workflow.Reject(id)); }
    catch (RuleException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.Run();

public record Row(string Id, string Folder, string Offense);
public record Complement(string Id, string Status);
public record Proposal(string Kind, string Period, Row[]? Rows, Complement[]? Complements,
    string? VictimId, string? NewOffense, string? Reason);
public record RegistryRow(string Id, string Folder, string Offense, string Status, string? Reason);
public record Pending(Guid Id, long BaseRevision, Proposal Request);
public record Audit(long Revision, string Kind, string Period, DateTimeOffset At,
    string? VictimId = null, string? PreviousOffense = null, string? NewOffense = null, string? Reason = null);
public record Cut(Row[] Monthly, RegistryRow[] Registry);
public record State(long Revision, Dictionary<string, Cut> Cuts, Pending? Pending, Audit[] Audit);
public class RuleException(string message) : Exception(message);

public sealed class Workflow
{
    private readonly object gate = new();
    private readonly Dictionary<string, Cut> cuts = [];
    private readonly List<Audit> audit = [];
    private Pending? pending;
    private long revision;

    public State Snapshot()
    {
        lock (gate)
            return Copy(new State(revision, cuts, pending, audit.ToArray()));
    }

    public Pending Propose(Proposal input)
    {
        lock (gate)
        {
            if (pending is not null) throw new RuleException("Hay una propuesta pendiente. Apruébela o rechácela primero.");
            var request = Copy(input);
            _ = Apply(request); // Valida sin modificar el estado aprobado.
            pending = new Pending(Guid.NewGuid(), revision, request);
            return Copy(pending);
        }
    }

    public State Approve(Guid id)
    {
        lock (gate)
        {
            var operation = RequirePending(id);
            if (operation.BaseRevision != revision) throw new RuleException("El estado cambió. Vuelva a validar.");
            var next = Apply(operation.Request); // Revalidación antes de integrar.
            var before = cuts.GetValueOrDefault(operation.Request.Period)?.Registry
                .SingleOrDefault(v => v.Id == operation.Request.VictimId);
            var after = next.Registry.SingleOrDefault(v => v.Id == operation.Request.VictimId);
            cuts[operation.Request.Period] = next;
            revision++;
            audit.Add(new Audit(revision, operation.Request.Kind, operation.Request.Period, DateTimeOffset.UtcNow,
                after?.Id, before?.Offense, after?.Offense, operation.Request.Reason));
            pending = null;
            return Snapshot();
        }
    }

    public State Reject(Guid id)
    {
        lock (gate)
        {
            var operation = RequirePending(id);
            revision++;
            audit.Add(new Audit(revision, "REJECT:" + operation.Request.Kind, operation.Request.Period, DateTimeOffset.UtcNow));
            pending = null;
            return Snapshot();
        }
    }

    private Pending RequirePending(Guid id) => pending is { } operation && operation.Id == id
        ? operation : throw new RuleException("La propuesta ya no está pendiente.");

    private Cut Apply(Proposal request)
    {
        if (!DateOnly.TryParseExact(request.Period + "-01", "yyyy-MM-dd", out _))
            throw new RuleException("El periodo debe tener formato AAAA-MM.");
        var previous = cuts.GetValueOrDefault(request.Period) ?? new Cut([], []);
        if (request.Kind == "MONTHLY") return Monthly(request, previous);
        if (request.Kind == "REGISTRY") return Registry(request, previous);
        if (request.Kind is "RECLASSIFY" or "REVERT") return Reclassify(request, previous);
        throw new RuleException("Tipo de operación desconocido.");
    }

    private static Cut Monthly(Proposal request, Cut previous)
    {
        var rows = request.Rows ?? [];
        if (rows.Length is < 1 or > 1000) throw new RuleException("La demo admite entre 1 y 1000 registros por corte.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is null || !ValidId(row.Id) || !ValidId(row.Folder) || !ids.Add(row.Id))
                throw new RuleException("Identificadores inválidos o repetidos. Use claves ficticias DEMO- de hasta 40 caracteres.");
            if (row.Offense is not ("DESAPARICION" or "OTRO"))
                throw new RuleException("Delito permitido en mensual: DESAPARICION u OTRO.");
        }
        var complements = request.Complements ?? [];
        if (complements.Any(c => c is null || !ValidId(c.Id) || c.Status is not ("NO_LOCALIZADA" or "LOCALIZADA"))
            || complements.Select(c => c.Id).Distinct().Count() != complements.Length)
            throw new RuleException("Complementarios inválidos o repetidos.");
        if (previous.Registry.Length == 0)
        {
            if (complements.Length != 0) throw new RuleException("Primero apruebe el mensual y después cargue BANCI.");
            return new Cut(rows, []);
        }
        foreach (var victim in previous.Registry)
        {
            var row = rows.SingleOrDefault(r => r.Id == victim.Id);
            if (row is not null && row.Folder != victim.Folder)
                throw new RuleException($"{victim.Id}: la carpeta debe coincidir con BANCI.");
            if (victim.Offense == "DESAPARICION" && row?.Offense != "DESAPARICION")
                throw new RuleException($"{victim.Id}: desaparición vigente; requiere reclasificación aprobada en BANCI.");
            if (victim.Offense != "DESAPARICION" && row is not null && row.Offense != victim.Offense)
                throw new RuleException($"{victim.Id}: el mensual contradice la reclasificación aprobada en BANCI.");
        }
        var newRows = rows.Where(r => r.Offense == "DESAPARICION" && !previous.Registry.Any(v => v.Id == r.Id)).ToArray();
        if (!newRows.Select(r => r.Id).Order().SequenceEqual(complements.Select(c => c.Id).Order()))
            throw new RuleException("Debe adjuntar complementarios BANCI exactamente para las desapariciones nuevas: " +
                string.Join(", ", newRows.Select(r => r.Id)));
        var registry = previous.Registry.Concat(newRows.Select(r => new RegistryRow(r.Id, r.Folder, r.Offense,
            complements.Single(c => c.Id == r.Id).Status, null))).ToArray();
        return new Cut(rows, registry);
    }

    private static Cut Registry(Proposal request, Cut previous)
    {
        if (previous.Monthly.Length == 0) throw new RuleException("Primero apruebe el corte mensual.");
        if (previous.Registry.Length != 0) throw new RuleException("BANCI ya está cargado para este corte.");
        var expected = previous.Monthly.Where(r => r.Offense == "DESAPARICION").ToArray();
        if (expected.Length == 0) throw new RuleException("No hay desapariciones para complementar.");
        var complements = request.Complements ?? [];
        if (complements.Any(c => c is null || c.Status is not ("NO_LOCALIZADA" or "LOCALIZADA")) ||
            !expected.Select(r => r.Id).Order().SequenceEqual(complements.Select(c => c.Id).Order()))
            throw new RuleException("BANCI debe complementar todas las desapariciones aprobadas, sin duplicados ni adicionales.");
        return new Cut(previous.Monthly, expected.Select(r => new RegistryRow(r.Id, r.Folder, r.Offense,
            complements.Single(c => c.Id == r.Id).Status, null)).ToArray());
    }

    private static Cut Reclassify(Proposal request, Cut previous)
    {
        var victim = previous.Registry.SingleOrDefault(v => v.Id == request.VictimId)
            ?? throw new RuleException("La víctima no existe en BANCI para el periodo.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
            throw new RuleException("Indique un motivo de 1 a 200 caracteres.");
        string offense;
        if (request.Kind == "REVERT")
        {
            if (victim.Offense == "DESAPARICION") throw new RuleException("La víctima ya es desaparición vigente.");
            offense = "DESAPARICION";
        }
        else
        {
            if (victim.Offense != "DESAPARICION") throw new RuleException("La víctima ya está reclasificada.");
            if (request.NewOffense is not ("OTRO" or "NO_DELITO")) throw new RuleException("Seleccione OTRO o NO_DELITO.");
            offense = request.NewOffense;
        }
        return new Cut(previous.Monthly, previous.Registry.Select(v => v.Id == victim.Id
            ? v with { Offense = offense, Reason = request.Reason } : v).ToArray());
    }

    private static bool ValidId(string? value) => value is { Length: >= 6 and <= 40 }
        && value.StartsWith("DEMO-", StringComparison.Ordinal)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
