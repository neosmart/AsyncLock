#if TRY_LOCK_OUT_BOOL

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    [TestClass]
    public class TryLockTestsAsyncOut
    {
        [TestMethod]
        public async Task NoContention()
        {
            var @lock = new AsyncLock();

            Assert.IsTrue(await @lock.TryLockAsync(() => { }, TimeSpan.Zero));
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
                using (await @lock.TryLockAsync(TimeSpan.Zero, out var locked))
                {
                    if (locked)
                    {
                        await Task.Yield();
                        throw new LocalException("This exception needs to be bubbled up");
                    }
                }
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
                    await Task.Yield();
                    var disposable = @lock.TryLockAsync(TimeSpan.Zero, out var locked);
                    Assert.IsFalse(locked);
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

                await @lock.TryLockAsync(TimeSpan.FromMilliseconds(lockTimeoutMs), out var locked);
                Assert.IsTrue((!expectedResult) ^ locked);

                if (locked)
                {
                    Assert.AreEqual(2, step);

                }
            });
            testThread.Start();

            unlockThread.Join();
            testThread.Join();
        }
    }
}

#endif
