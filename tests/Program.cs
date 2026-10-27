var workflow = new Workflow();
var checks = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Block(Proposal request) {
    var revision = workflow.Snapshot().Revision;
    try { workflow.Propose(request); throw new Exception("Se aceptó una operación inválida."); }
    catch (RuleException) { Assert(workflow.Snapshot().Revision == revision, "La validación modificó el estado."); }
}
void Apply(Proposal request) {
    var pending = workflow.Propose(request);
    workflow.Approve(pending.Id);
}
var rows = new[] { new Row("DEMO-V001", "DEMO-C001", "DESAPARICION"), new Row("DEMO-V002", "DEMO-C001", "DESAPARICION") };
Proposal Monthly(Row[] data, Complement[]? complements = null) => new("MONTHLY", "2026-09", data, complements, null, null, null);
Proposal Reclass(string kind = "RECLASSIFY") => new(kind, "2026-09", null, null, "DEMO-V001", "NO_DELITO", "Motivo ficticio de demostración");
Block(Monthly([rows[0], rows[0]]));
Block(Monthly([rows[0] with { Folder = "INVALID" }]));
Block(new("MONTHLY", "2026-13", rows, null, null, null, null));
var first = workflow.Propose(Monthly(rows));
Assert(workflow.Snapshot().Cuts.Count == 0, "La propuesta se aplicó antes de aprobar.");
rows[0] = rows[0] with { Folder = "DEMO-ALTERADA" };
Assert(workflow.Snapshot().Pending!.Request.Rows![0].Folder == "DEMO-C001", "Referencia mutable en staging.");
rows[0] = rows[0] with { Folder = "DEMO-C001" };
Block(Monthly(rows));
workflow.Approve(first.Id);
var complements = rows.Select(r => new Complement(r.Id, "NO_LOCALIZADA")).ToArray();
Block(new("REGISTRY", "2026-09", null, [complements[0]], null, null, null));
Apply(new("REGISTRY", "2026-09", null, complements, null, null, null));
Block(Monthly([rows[1]]));
var reclass = workflow.Propose(Reclass());
Assert(workflow.Snapshot().Cuts["2026-09"].Registry.All(v => v.Offense == "DESAPARICION"), "Reclasificación anticipada.");
workflow.Approve(reclass.Id);
Assert(workflow.Snapshot().Cuts["2026-09"].Registry.Count(v => v.Offense == "DESAPARICION") == 1, "Debe afectar una sola víctima.");
Assert(workflow.Snapshot().Cuts["2026-09"].Monthly.Length == 2, "BANCI alteró el mensual.");
Block(Monthly(rows));
var added = new Row("DEMO-V003", "DEMO-C002", "DESAPARICION");
Block(Monthly([rows[1], added]));
Block(Monthly([rows[1] with { Folder = "DEMO-C999" }, added], [new(added.Id, "LOCALIZADA")]));
Apply(Monthly([rows[1], added], [new(added.Id, "LOCALIZADA")]));
Assert(workflow.Snapshot().Cuts["2026-09"].Registry.Length == 3, "Se perdió el histórico de BANCI.");
Apply(Reclass("REVERT"));
Block(Monthly([rows[1], added]));
Apply(Monthly([rows[0], rows[1], added]));
var rejected = workflow.Propose(Reclass());
workflow.Reject(rejected.Id);
Assert(workflow.Snapshot().Cuts["2026-09"].Registry.All(v => v.Offense == "DESAPARICION"), "Rechazar modificó BANCI.");
try { workflow.Approve(rejected.Id); throw new Exception("Aprobación duplicada."); }
catch (RuleException) { checks++; }
var snapshot = workflow.Snapshot();
snapshot.Cuts.Clear();
Assert(workflow.Snapshot().Cuts.Count == 1, "Snapshot no aislado.");
Assert(workflow.Snapshot().Audit.Length == workflow.Snapshot().Revision, "Auditoría incompleta.");
Console.WriteLine($"OK: {checks} comprobaciones del flujo mensual/BANCI.");
