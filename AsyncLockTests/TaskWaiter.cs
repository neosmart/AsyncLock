using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    /// <summary>
    /// A guaranteed-safe method of "synchronously" waiting on tasks to finish. Cannot be used on .NET Core
    /// </summary>
    class TaskWaiter : EventWaitHandle
    {
        public TaskWaiter(Task task)
            : base(false, EventResetMode.ManualReset)
        {
            new Thread(async () =>
            {
                await task;
                Set();
            }).Start();
        }
    }
}
