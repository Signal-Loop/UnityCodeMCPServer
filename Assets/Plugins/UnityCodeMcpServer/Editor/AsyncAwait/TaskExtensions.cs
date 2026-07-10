using System;
using System.Threading.Tasks;
using UnityCodeMcpServer.Helpers;

namespace UnityCodeMcpServer.AsyncAwait
{
    public static class TaskExtensions
    {
        public static async void Forget(this Task task, string operationName = null)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                string prefix = string.IsNullOrWhiteSpace(operationName)
                    ? "[AsyncAwait] Detached task failed"
                    : $"[AsyncAwait] Detached task failed ({operationName})";
                UnityCodeMcpServerLogger.Error($"{prefix}: {ex}");
            }
        }
    }
}
