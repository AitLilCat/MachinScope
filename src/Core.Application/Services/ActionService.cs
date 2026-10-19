using Core.Application.Interfaces;
using Core.Domain.Models;
namespace Core.Application.Services;

// Serial execution is deliberate: per-item validation occurs immediately before each backend call.
// Native backends must still validate the target using the handle used for mutation.
public sealed class ActionService
{
    public async Task<IReadOnlyList<ActionItemResult>> ExecuteAsync<T>(ICoreAction<T> action, IReadOnlyList<T> targets, IReadOnlySet<string> confirmedIdentities, IProgress<ActionProgress>? progress, CancellationToken token)
    {
        var results = new List<ActionItemResult>(targets.Count);
        foreach (var target in targets)
        {
            var identity = action.Identity(target);
            try
            {
                if (token.IsCancellationRequested) { results.Add(new(identity, ActionOutcome.Cancelled, "Not executed: cancelled")); continue; }
                if (action.Risk.HasFlag(ActionRisk.Destructive) && !confirmedIdentities.Contains(identity)) { results.Add(new(identity, ActionOutcome.Denied, "Exact target confirmation is required")); continue; }
                var decision = action.Validate(target);
                if (!decision.Allowed) { results.Add(new(identity, ActionOutcome.Denied, decision.Reason)); continue; }
                var message = await action.ExecuteAsync(target, token);
                results.Add(new(identity, ActionOutcome.Succeeded, message));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { results.Add(new(identity, ActionOutcome.Cancelled, "Cancelled; inspect target state before retrying")); }
            catch (Exception e) { results.Add(new(identity, ActionOutcome.Failed, e.Message)); }
            finally { progress?.Report(new(results.Count, targets.Count, identity)); }
        }
        return results;
    }
}
public enum ProcessOperation { End, Restart, Priority, Affinity }
public sealed class ProcessAction(IProcessController controller, ProcessOperation operation, string priority = "Normal", ulong affinity = 0) : ICoreAction<ProcessSample>
{
    public string Name => operation.ToString();
    public ActionRisk Risk => operation is ProcessOperation.End or ProcessOperation.Restart ? ActionRisk.Destructive : ActionRisk.LowRisk;
    public string Identity(ProcessSample target) => target.Identity;
    public ActionDecision Validate(ProcessSample target)
    {
        if (!target.Started.HasValue) return ActionDecision.Deny("Process creation time is unavailable");
        if (target.Id <= 4 || target.Id == Environment.ProcessId) return ActionDecision.Deny("Protected system or MachinScope process");
        if (operation == ProcessOperation.Restart) { var available = controller.CanRestart(target); if (!available.Available) return ActionDecision.Deny(available.Reason); }
        return ActionDecision.Permit;
    }
    public Task<string> ExecuteAsync(ProcessSample target, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        switch (operation) { case ProcessOperation.End: controller.EndProcess(target, false); break; case ProcessOperation.Restart: controller.RestartProcess(target); break; case ProcessOperation.Priority: controller.SetPriority(target, priority); break; case ProcessOperation.Affinity: controller.SetAffinity(target, affinity); break; }
        return "Succeeded";
    }, token);
}
