using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if false
namespace AsyncLockTests
{
    [TestClass]
    public class AsyncIdTests
    {
        [TestMethod]
        public void TaskIdUniqueness()
        {
            var testCount = 100;
            var countdown = new CountdownEvent(testCount);
            var failure = new ManualResetEventSlim(false);
            var threadIds = new SortedSet<long>();
            var abort = new SemaphoreSlim(0, 1);

            for (int i = 0; i < testCount; ++i)
            {
                Task.Run(async () =>
                {
                    lock (threadIds)
                    {
                        if (!threadIds.Add(AsyncLock.ThreadId))
                        {
                            failure.Set();
                        }
                    }
                    countdown.Signal();
                    await abort.WaitAsync();
                });
            }

            if (WaitHandle.WaitAny(new[] { countdown.WaitHandle, failure.WaitHandle }) == 1)
            {
                Assert.Fail("A duplicate thread id was found!");
            }

            abort.Release();
        }

        public void ThreadIdUniqueness()
        {
            var testCount = 100;
            var countdown = new CountdownEvent(testCount);
            var failure = new ManualResetEventSlim(false);
            var threadIds = new SortedSet<long>();
            var abort = new SemaphoreSlim(0, 1);

            for (int i = 0; i < testCount; ++i)
            {
                Task.Run(async () =>
                {
                    lock (threadIds)
                    {
                        if (!threadIds.Add(AsyncLock.ThreadId))
                        {
                            failure.Set();
                        }
                    }
                    countdown.Signal();
                    await abort.WaitAsync();
                });
            }

            if (WaitHandle.WaitAny(new[] { countdown.WaitHandle, failure.WaitHandle }) == 1)
            {
                Assert.Fail("A duplicate thread id was found!");
            }

            abort.Release();
        }
    }
}
#endif
