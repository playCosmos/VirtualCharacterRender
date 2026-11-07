using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using VCR.Runtime.UI;

namespace VCR.Editor.P1
{
    // Source-free regression for the UI Toolkit's threaded log queue.
    // Reflects the real shell implementation rather than reimplementing it
    // in a separate test-only queue class.
    internal static class P1WorkspaceLogValidation
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        public static bool RunChecks()
        {
            GameObject root = null;
            object shell = null;
            MethodInfo unsubscribe = null;
            var subscribed = false;
            try
            {
                root = new GameObject("P1 Workspace Log Queue Validation");
                root.SetActive(false);
                shell = root.AddComponent<WorkspaceToolkitShell>();
                var type = shell.GetType();
                var subscribe = RequireMethod(type, "SubscribeWorkspaceLog");
                unsubscribe = RequireMethod(type, "UnsubscribeWorkspaceLog");
                var log = RequireMethod(type, "OnWorkspaceLogReceived");
                var reset = RequireMethod(type, "ResetWorkspaceLogPanel");
                var clear = RequireMethod(type, "ClearWorkspacePendingLogs");
                var gate = RequireField(type, "_workspacePendingLogSync").GetValue(shell);
                var queue = RequireField(type, "_workspacePendingLogs").GetValue(shell);
                var countField = RequireField(type, "_workspacePendingLogCount");
                var droppedField = RequireField(type, "_workspaceDroppedLogCount");
                var queueCount = queue.GetType().GetProperty("Count");
                if (gate == null || queueCount == null)
                    throw new InvalidOperationException("Missing queue lock or Count property");

                subscribe.Invoke(shell, null);
                subscribed = true;
                for (var i = 0; i < 650; i++)
                    log.Invoke(shell, new object[] { "P1-log-stress", "", LogType.Log });
                CheckQueueInvariant(shell, gate, queue, queueCount, countField,
                    droppedField, expectDrops: true);

                // Exercise the earlier race: worker callbacks may arrive while
                // the main thread clears/restarts the panel. Counts must match
                // the queue after both sides finish.
                reset.Invoke(shell, null);
                var workers = new Task[4];
                for (var worker = 0; worker < workers.Length; worker++)
                {
                    workers[worker] = Task.Run(() =>
                    {
                        for (var i = 0; i < 500; i++)
                            log.Invoke(shell,
                                new object[] { "P1-worker-log", "", LogType.Warning });
                    });
                }
                for (var i = 0; i < 48; i++)
                {
                    reset.Invoke(shell, null);
                    CheckQueueInvariant(shell, gate, queue, queueCount, countField,
                        droppedField, expectDrops: false);
                }
                Task.WaitAll(workers);
                CheckQueueInvariant(shell, gate, queue, queueCount, countField,
                    droppedField, expectDrops: false);

                clear.Invoke(shell, null);
                lock (gate)
                {
                    if ((int)countField.GetValue(shell) != 0 ||
                        (int)queueCount.GetValue(queue) != 0 ||
                        (int)droppedField.GetValue(shell) != 0)
                        throw new InvalidOperationException(
                            "Clear must discard pending events and reset counters");
                }

                unsubscribe.Invoke(shell, null);
                subscribed = false;
                log.Invoke(shell, new object[] { "late-callback", "", LogType.Log });
                lock (gate)
                {
                    if ((int)countField.GetValue(shell) != 0 ||
                        (int)queueCount.GetValue(queue) != 0)
                        throw new InvalidOperationException(
                            "Late callbacks must not enqueue after unsubscribe");
                }

                Debug.Log("VCR P1 workspace threaded-log queue validation: PASS.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P1 workspace threaded-log queue validation: FAIL: " +
                               exception);
                return false;
            }
            finally
            {
                if (subscribed && unsubscribe != null && shell != null)
                {
                    try { unsubscribe.Invoke(shell, null); }
                    catch (Exception error)
                    {
                        Debug.LogError("VCR P1 log test unsubscribe failed: " + error);
                    }
                }
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CheckQueueInvariant(
            object shell, object gate, object queue, PropertyInfo queueCount,
            FieldInfo count, FieldInfo dropped, bool expectDrops)
        {
            lock (gate)
            {
                var pending = (int)count.GetValue(shell);
                var observed = (int)queueCount.GetValue(queue);
                var discarded = (int)dropped.GetValue(shell);
                if (pending != observed || pending < 0 || pending > 512 ||
                    discarded < 0 || (expectDrops && discarded == 0))
                    throw new InvalidOperationException(
                        "Bounded log queue invariant failed: pending=" + pending +
                        ", actual=" + observed + ", dropped=" + discarded);
            }
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return type.GetMethod(name, PrivateInstance) ??
                throw new MissingMethodException(type.FullName, name);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            return type.GetField(name, PrivateInstance) ??
                throw new MissingFieldException(type.FullName, name);
        }
    }
}
