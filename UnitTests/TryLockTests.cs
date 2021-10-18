using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Threading;

namespace AsyncLockTests
{
    [TestClass]
    public class TryLockTests
    {
        [TestMethod]
        public void NoContention()
        {
            var @lock = new AsyncLock();

            Assert.IsTrue(@lock.TryLock(() => { }, default));
        }

        [TestMethod]
        public void ContentionEarlyReturn()
        {
            var @lock = new AsyncLock();

            using (@lock.Lock())
            {
                var thread = new Thread(() =>
                {
                    Assert.IsFalse(@lock.TryLock(() => throw new Exception("This should never be executed"), default));
                });
                thread.Start();
                thread.Join();
            }
        }

        [TestMethod]
        public void ContentionDelayedExecution() => ContentionalExecution(50, 250, true);

        [TestMethod]
        public void ContentionNoExecution() => ContentionalExecution(250, 50, false);

        [TestMethod]
        public void ContentionNoExecutionZeroTimeout() => ContentionalExecution(250, 0, false);

        private void ContentionalExecution(int unlockDelayMs, int lockTimeoutMs, bool expectedResult)
        {
            int step = 0;
            var @lock = new AsyncLock();

            var locked = @lock.Lock();
            Interlocked.Increment(ref step);

            using var eventTestThreadStarted = new AutoResetEvent(false);
            using var eventSleepNotStarted = new AutoResetEvent(false);
            using var eventAboutToWait = new AutoResetEvent(false);

            var unlockThread = new Thread(() =>
            {
                eventTestThreadStarted.WaitOne();
                eventSleepNotStarted.Set();
                Thread.Sleep(unlockDelayMs);
                eventAboutToWait.WaitOne();
                Interlocked.Increment(ref step);
                locked.Dispose();
            });
            unlockThread.Start();

            var testThread = new Thread(() =>
            {
                eventTestThreadStarted.Set();
                eventSleepNotStarted.WaitOne();
                eventAboutToWait.Set();
                Assert.IsTrue((!expectedResult) ^ @lock.TryLock(() =>
                {
                    Assert.AreEqual(2, step);
                }, TimeSpan.FromMilliseconds(lockTimeoutMs)));
            });
            testThread.Start();

            unlockThread.Join();
            testThread.Join();
        }
    }
}
