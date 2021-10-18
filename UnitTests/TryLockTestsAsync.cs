using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    class LocalException : Exception {
        public LocalException(string message) : base(message) { }
    }

    [TestClass]
    public class TryLockTestsAsync
    {
        [TestMethod]
        public async Task NoContention()
        {
            var @lock = new AsyncLock();

            Assert.IsTrue(await @lock.TryLockAsync(default, () => { }));
        }

        /// <summary>
        /// Assert that exceptions are bubbled up after the lock is disposed
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task NoContentionThrows()
        {
            var @lock = new AsyncLock();

            await Assert.ThrowsExceptionAsync<LocalException>(async () =>
            {
                await @lock.TryLockAsync(default, async () => {
                    await Task.Yield();
                    throw new LocalException("This exception needs to be bubbled up");
                });
            });
        }

        [TestMethod]
        public async Task ContentionEarlyReturn()
        {
            var @lock = new AsyncLock();

            using (await @lock.LockAsync())
            {
                var thread = new Thread(async () =>
                {
                    Assert.IsFalse(await @lock.TryLockAsync(TimeSpan.FromMilliseconds(0), () => throw new Exception("This should never be executed")));
                });
                thread.Start();
                thread.Join();
            }
        }

        [TestMethod]
        public async Task ContentionDelayedExecution() => await ContentionalExecution(50, 250, true);

        [TestMethod]
        public async Task ContentionNoExecution() => await ContentionalExecution(250, 50, false);

        [TestMethod]
        public async Task ContentionNoExecutionZeroTimeout() => await ContentionalExecution(250, 0, false);

        private async Task ContentionalExecution(int unlockDelayMs, int lockTimeoutMs, bool expectedResult)
        {
            int step = 0;
            var @lock = new AsyncLock();

            var locked = await @lock.LockAsync();
            Interlocked.Increment(ref step);

            using var eventTestThreadStarted = new SemaphoreSlim(0, 1);
            using var eventSleepNotStarted = new SemaphoreSlim(0, 1);
            using var eventAboutToWait = new SemaphoreSlim(0, 1);

            var unlockThread = new Thread(async () =>
            {
                await eventTestThreadStarted.WaitAsync();
                eventSleepNotStarted.Release();
                Thread.Sleep(unlockDelayMs);
                await eventAboutToWait.WaitAsync();
                Interlocked.Increment(ref step);
                locked.Dispose();
            });
            unlockThread.Start();

            var testThread = new Thread(async () =>
            {
                eventTestThreadStarted.Release();
                await eventSleepNotStarted.WaitAsync();
                eventAboutToWait.Release();
                Assert.IsTrue((!expectedResult) ^ await @lock.TryLockAsync(TimeSpan.FromMilliseconds(lockTimeoutMs), () =>
                {
                    Assert.AreEqual(2, step);
                }));
            });
            testThread.Start();

            unlockThread.Join();
            testThread.Join();
        }
    }
}
