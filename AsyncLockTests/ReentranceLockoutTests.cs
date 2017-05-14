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

        void ThreadEntryPoint()
        {
            using (_lock.Lock())
            {
                _resource.BeginSomethingDangerous();
                Thread.Sleep(new Random().Next(0, 10) * 10);
                _resource.EndSomethingDangerous();
            }
            _countdown.Tick();
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing a function entrypoint.
        /// </summary>
        [TestMethod]
        public void MultipleThreadsMethodLockout()
        {
            _lock = new AsyncLock();
            //start n threads and have them obtain the lock and randomly wait, then verify
            _resource = new LimitedResource(() =>
            {
                Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
            });

            var testCount = 20;
            _countdown = new CountdownEvent(testCount);
            for (int i = 0; i < testCount; ++i)
            {
                var t = new Thread(ThreadEntryPoint);
                t.Start();
            }

            _countdown.WaitOne();
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing nothing.
        /// </summary>
        [TestMethod]
        public void MultipleThreadsLockout()
        {
            _lock = new AsyncLock();
            //start n threads and have them obtain the lock and randomly wait, then verify
            _resource = new LimitedResource(() =>
            {
                Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
            });

            var testCount = 20;
            _countdown = new CountdownEvent(testCount);
            for (int i = 0; i < testCount; ++i)
            {
                var t = new Thread(() =>
                {
                    using (_lock.Lock())
                    {
                        _resource.BeginSomethingDangerous();
                        Thread.Sleep(new Random().Next(0, 10) * 10);
                        _resource.EndSomethingDangerous();
                    }
                    _countdown.Tick();
                });
                t.Start();
            }

            _countdown.WaitOne();
        }

        /// <summary>
        /// Tests whether the lock successfully prevents multiple threads from obtaining a lock simultaneously when sharing a local ThreadStart
        /// </summary>
        [TestMethod]
        public void MultipleThreadsThreadStartLockout()
        {
            _lock = new AsyncLock();
            //start n threads and have them obtain the lock and randomly wait, then verify
            _resource = new LimitedResource(() =>
            {
                Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
            });

            ThreadStart work = () =>
            {
                using (_lock.Lock())
                {
                    _resource.BeginSomethingDangerous();
                    Thread.Sleep(new Random().Next(0, 10) * 10);
                    _resource.EndSomethingDangerous();
                }
                _countdown.Tick();
            };

            var testCount = 20;
            _countdown = new CountdownEvent(testCount);
            for (int i = 0; i < testCount; ++i)
            {
                var t = new Thread(work);
                t.Start();
            }

            _countdown.WaitOne();
        }

        [TestMethod]
        public void AsyncLockout()
        {
            _lock = new AsyncLock();
            //start n threads and have them obtain the lock and randomly wait, then verify
            _resource = new LimitedResource(() =>
            {
                Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
            });

            var testCount = 20;
            _countdown = new CountdownEvent(testCount);
            for (int i = 0; i < testCount; ++i)
            {
                Task.Run(async () =>
                {
                    using (await _lock.LockAsync())
                    {
                        _resource.BeginSomethingDangerous();
                        Thread.Sleep(new Random().Next(0, 10) * 10);
                        _resource.EndSomethingDangerous();
                    }
                    _countdown.Tick();
                });
            }

            //MSTest does not support async test methods (apparently, but I could be wrong)
            //await Task.WhenAll(tasks);
            _countdown.WaitOne();
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
