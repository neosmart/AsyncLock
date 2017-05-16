using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;

namespace AsyncLockTests
{
    [TestClass]
    public class ReentranceLockoutTests
    {
        private AsyncLock _lock;
        private LimitedResource _resource;
        private CountdownEvent _countdown;
        private Random _random = new Random((int)DateTime.UtcNow.Ticks);
        private int DelayInterval => _random.Next(5, 10) * 10;

        private void ResourceSimulation(Action action)
        {
            _lock = new AsyncLock();
            //start n threads and have them obtain the lock and randomly wait, then verify
            var failure = new ManualResetEventSlim(false);
            _resource = new LimitedResource(() =>
            {
                failure.Set();
            });

            var testCount = 20;
            _countdown = new CountdownEvent(testCount);

            for (int i = 0; i < testCount; ++i)
            {
                action();
            }

            //MSTest does not support async test methods (apparently, but I could be wrong)
            //await Task.WhenAll(tasks);
            if (WaitHandle.WaitAny(new[] { _countdown.WaitHandle, failure.WaitHandle }) == 1)
            {
                Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
            }
        }

        private void ThreadEntryPoint()
        {
            using (_lock.Lock())
            {
                _resource.BeginSomethingDangerous();
                Thread.Sleep(DelayInterval);
                _resource.EndSomethingDangerous();
            }
            _countdown.Signal();
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing a function entrypoint.
        /// </summary>
        [TestMethod]
        public void MultipleThreadsMethodLockout()
        {
            ResourceSimulation(() =>
            {
                var t = new Thread(ThreadEntryPoint);
                t.Start();
            });
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing nothing.
        /// </summary>
        [TestMethod]
        public void MultipleThreadsLockout()
        {
            ResourceSimulation(() =>
            {
                var t = new Thread(() =>
                {
                    using (_lock.Lock())
                    {
                        _resource.BeginSomethingDangerous();
                        Thread.Sleep(DelayInterval);
                        _resource.EndSomethingDangerous();
                    }
                    _countdown.Signal();
                });
                t.Start();
            });
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing a local ThreadStart
        /// </summary>
        [TestMethod]
        public void MultipleThreadsThreadStartLockout()
        {
            ThreadStart work = () =>
            {
                using (_lock.Lock())
                {
                    _resource.BeginSomethingDangerous();
                    Thread.Sleep(DelayInterval);
                    _resource.EndSomethingDangerous();
                }
                _countdown.Signal();
            };

            ResourceSimulation(() =>
            {
                var t = new Thread(work);
                t.Start();
            });
        }

        [TestMethod]
        public void AsyncLockout()
        {
            ResourceSimulation(() =>
            {
                Task.Run(async () =>
                {
                    using (await _lock.LockAsync())
                    {
                        _resource.BeginSomethingDangerous();
                        Thread.Sleep(DelayInterval);
                        _resource.EndSomethingDangerous();
                    }
                    _countdown.Signal();
                });
            });
        }

        [TestMethod]
        public void AsyncDelayLockout()
        {
            ResourceSimulation(() =>
            {
                Task.Run(async () =>
                {
                    using (await _lock.LockAsync())
                    {
                        _resource.BeginSomethingDangerous();
                        await Task.Delay(DelayInterval);
                        _resource.EndSomethingDangerous();
                    }
                    _countdown.Signal();
                });
            });
        }

        [TestMethod]
        public void NestedAsyncLockout()
        {
            var taskStarted = new ManualResetEventSlim(false);
            var @lock = new AsyncLock();
            using (@lock.Lock())
            {
                var task = Task.Run(async () =>
                {
                    taskStarted.Set();
                    using (await @lock.LockAsync())
                    {
                        Debug.WriteLine("Hello from within an async task!");
                    }
                });

                taskStarted.Wait();
                Assert.IsFalse(new TaskWaiter(task).WaitOne(100));
            }
        }
    }
}
