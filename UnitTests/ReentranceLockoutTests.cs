using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;

namespace AsyncLockTests;

[TestClass]
public class ReentranceLockoutTests
{
    private AsyncLock _lock;
    private LimitedResource _resource;
    private CountdownEvent _countdown;
    private readonly Random _random = new((int)DateTime.UtcNow.Ticks);
    private int DelayInterval => _random.Next(1, 5) * 10;

    private void ResourceSimulation(Action action)
    {
        _lock = new AsyncLock();
        // Start n threads and have them obtain the lock and randomly wait, then verify
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

        if (WaitHandle.WaitAny([_countdown.WaitHandle, failure.WaitHandle]) == 1)
        {
            Assert.Fail("More than one thread simultaneously accessed the underlying resource!");
        }
    }

    private async void ThreadEntryPoint()
    {
        using (await _lock.LockAsync())
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
            var t = new Thread(async () =>
            {
                using (await _lock.LockAsync())
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
        ThreadStart work = async () =>
        {
            using (await _lock.LockAsync())
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
    public async Task NestedAsyncLockout()
    {
        var taskStarted = new SemaphoreSlim(0, 1);
        var taskEnded = new SemaphoreSlim(0, 1);
        var @lock = new AsyncLock();
        using (await @lock.LockAsync())
        {
            var task = Task.Run(async () =>
            {
                taskStarted.Release();
                using (await @lock.LockAsync())
                {
                    Debug.WriteLine("Hello from within an async task!");
                }
                await taskEnded.WaitAsync();
            });

            taskStarted.Wait();
            Assert.IsFalse(new TaskWaiter(task).WaitOne(100));
            taskEnded.Release();
        }
    }
}
