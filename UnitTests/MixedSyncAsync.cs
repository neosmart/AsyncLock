using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    /// <summary>
    /// Creates multiple indepndent tasks, each with its own lock, and runs them
    /// all in parallel. There should be no contention for the lock between the
    /// parallelly executed tasks, but each task then recursively obtains what
    /// should be the same lock - which should again be contention-free - after
    /// an await point that may or may not resume on the same actual thread the
    /// previous lock was obtained with.
    /// </summary>
    [TestClass]
    public class MixedSyncAsync
    {
        [TestMethod]
        public async Task MixedSyncAsyncExecution()
        {
            var count = 0;
            var threads = new List<Thread>(10);
            var tasks = new List<Task>(10);
            var asyncLock = new AsyncLock();
            var rng = new Random();

            {
                using var l = asyncLock.Lock();
                for (int i = 0; i < 10; ++i)
                {
                    var thread = new Thread(() =>
                    {
                        using (asyncLock.Lock())
                        {
                            Assert.AreEqual(Interlocked.Increment(ref count), 1);
                            Thread.Sleep(rng.Next(1, 10) * 10);
                            using (asyncLock.Lock())
                            {
                                Thread.Sleep(10);
                                Assert.AreEqual(Interlocked.Decrement(ref count), 0);
                            }

                            Assert.AreEqual(count, 0);
                        }

                    });
                    thread.Start();
                    threads.Add(thread);
                }

                for (int i = 0; i < 10; ++i)
                {
                    var task = Task.Run(async () =>
                    {
                        using (await asyncLock.LockAsync())
                        {
                            Assert.AreEqual(Interlocked.Increment(ref count), 1);
                            Assert.AreEqual(count, 1);
                            await Task.Delay(rng.Next(1, 10) * 10);
                            using (await asyncLock.LockAsync())
                            {
                                await Task.Delay(10);
                                Assert.AreEqual(Interlocked.Decrement(ref count), 0);
                            }

                            Assert.AreEqual(count, 0);
                        }

                    });
                    tasks.Add(task);
                }
            }

            await Task.WhenAll(tasks);
            foreach (var thread in threads)
            {
                thread.Join();
            }

            Assert.AreEqual(count, 0);
        }
    }
}
