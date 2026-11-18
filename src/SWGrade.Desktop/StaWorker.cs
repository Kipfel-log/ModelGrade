using System;
using System.Threading;
using System.Threading.Tasks;

namespace SWGrade.Desktop
{
    internal static class StaWorker
    {
        internal static Task<T> Run<T>(Func<T> operation)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    completion.SetResult(operation());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            })
            {
                Name = "SWGrade SOLIDWORKS STA",
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }
    }
}
