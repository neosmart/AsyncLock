using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests;

/// <summary>
/// Creates multiple indepndent tasks, each with its own lock, and runs them
/// all in parallel. There should be no contention for the lock between the
/// parallelly executed tasks, but each task then recursively obtains what
/// should be the same lock - which should again be contention-free - after
/// an await point that may or may not resume on the same actual thread the
/// previous lock was obtained with.
/// </summary>
[TestClass]
public class MixedSyncAsyncTimed
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
                        Assert.AreEqual(1, Interlocked.Increment(ref count));
                        Thread.Sleep(rng.Next(1, 10) * 10);
                        using (asyncLock.Lock())
                        {
                            Thread.Sleep(10);
                            Assert.AreEqual(0, Interlocked.Decrement(ref count));
                        }

                        Assert.AreEqual(0, count);
                    }

                });
                thread.Start();
                threads.Add(thread);
            }

            for (int i = 0; i < 10; ++i)
            {
                var captured = i;
                var task = Task.Run(async () =>
                {
                    using (await asyncLock.LockAsync())
                    {
                        Assert.AreEqual(1, Interlocked.Increment(ref count));
                        Assert.AreEqual(1, count);
                        await Task.Delay(rng.Next(1, 10) * 10);
                        if (captured % 2 == 0)
                        {
                            using (await asyncLock.LockAsync())
                            {
                                await Task.Yield();
                                Assert.AreEqual(0, Interlocked.Decrement(ref count));
                            }
                        }
                        else
                        {
                            var executed = await asyncLock.TryLockAsync(async () =>
                            {
                                // Throw in a recursive async lock invocation
                                bool nestedExecuted = await asyncLock.TryLockAsync(async () =>
                                {
                                    await Task.Yield();
                                    Interlocked.Increment(ref count);
                                }, TimeSpan.FromMilliseconds(1 /* guarantees no zero-ms optimizations */));
                                Assert.IsTrue(nestedExecuted);
                                Interlocked.Decrement(ref count);
                                await Task.Yield();
                                Assert.AreEqual(0, Interlocked.Decrement(ref count));
                            }, TimeSpan.FromMilliseconds(rng.Next(1, 10) * 10));
                            Assert.IsTrue(executed, "TryLockAsync() did not end up executing!");
                        }

                        Assert.AreEqual(0, count);
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

        Assert.AreEqual(0, count);
    }
}
