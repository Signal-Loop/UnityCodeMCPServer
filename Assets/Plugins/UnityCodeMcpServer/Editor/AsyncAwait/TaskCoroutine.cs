using System;
using System.Collections;
using System.Threading.Tasks;

namespace UnityCodeMcpServer.AsyncAwait
{
    public static class TaskCoroutine
    {
        public static IEnumerator ToCoroutine(Func<Task> asyncAction)
        {
            if (asyncAction == null)
            {
                throw new ArgumentNullException(nameof(asyncAction));
            }

            Task task = asyncAction();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            task.GetAwaiter().GetResult();
        }
    }
}
