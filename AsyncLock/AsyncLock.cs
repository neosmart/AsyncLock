using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NeoSmart.AsyncLock
{
    public class AsyncLock
    {
        private SemaphoreSlim _reentrancy = new SemaphoreSlim(1, 1);
        private int _reentrances = 0;
        // We are using this SemaphoreSlim like a posix condition variable.
        // We only want to wake waiters, one or more of whom will try to obtain
        // a different lock to do their thing. So long as we can guarantee no
        // wakes are missed, the number of awakees is not important.
        // Ideally, this would be "friend" for access only from InnerLock, but
        // whatever.
        internal SemaphoreSlim _retry = new SemaphoreSlim(0, 1);
        private const long UnlockedId = 0x00; // "owning" task id when unlocked
        internal long _owningId = UnlockedId;
        internal int _owningThreadId = (int) UnlockedId;
        private static long AsyncStackCounter = 0;
        // An AsyncLocal<T> is not really the task-based equivalent to a ThreadLocal<T>, in that
        // it does not track the async flow (as the documentation describes) but rather it is
        // associated with a stack snapshot. Mutation of the AsyncLocal in an await call does
        // not change the value observed by the parent when the call returns, so if you want to
        // use it as a persistent async flow identifier, the value needs to be set at the outer-
        // most level and never touched internally.
        private static readonly AsyncLocal<long> _asyncId = new AsyncLocal<long>();
        private static long AsyncId => _asyncId.Value;

#if NETSTANDARD1_3
        private static int ThreadCounter = 0x00;
        private static ThreadLocal<int> LocalThreadId = new ThreadLocal<int>(() => ++ThreadCounter);
        private static int ThreadId => LocalThreadId.Value;
#else
        private static int ThreadId => Thread.CurrentThread.ManagedThreadId;
#endif

        public AsyncLock()
        {
        }

#if !DEBUG
        readonly
#endif
        struct InnerLock : IDisposable
        {
            private readonly AsyncLock _parent;
            private readonly long _oldId;
            private readonly int _oldThreadId;
#if DEBUG
            private bool _disposed;
#endif

            internal InnerLock(AsyncLock parent, long oldId, int oldThreadId)
            {
                _parent = parent;
                _oldId = oldId;
                _oldThreadId = oldThreadId;
#if DEBUG
                _disposed = false;
#endif
            }

            internal async Task<IDisposable> ObtainLockAsync(CancellationToken ct = default)
            {
                while (true)
                {
                    await _parent._reentrancy.WaitAsync(ct).ConfigureAwait(false);
                    if (InnerTryEnter(synchronous: false))
                    {
                        break;
                    }
                    // We need to wait for someone to leave the lock before trying again.
                    // We need to "atomically" obtain _retry and release _reentrancy, but there
                    // is no equivalent to a condition variable. Instead, we call *but don't await*
                    // _retry.WaitAsync(), then release the reentrancy lock, *then* await the saved task.
                    var waitTask = _parent._retry.WaitAsync(ct).ConfigureAwait(false);
                    _parent._reentrancy.Release();
                    await waitTask;
                }
                // Reset the owning thread id after all await calls have finished, otherwise we
                // could be resumed on a different thread and set an incorrect value.
                _parent._owningThreadId = ThreadId;
                _parent._reentrancy.Release();
                return this;
            }

            internal async Task<IDisposable?> TryObtainLockAsync(TimeSpan timeout)
            {
                // In case of zero-timeout, don't even wait for protective lock contention
                if (timeout == TimeSpan.Zero)
                {
                    _parent._reentrancy.Wait(timeout);
                    if (InnerTryEnter(synchronous: false))
                    {
                        // Reset the owning thread id after all await calls have finished, otherwise we
                        // could be resumed on a different thread and set an incorrect value.
                        _parent._owningThreadId = ThreadId;
                        _parent._reentrancy.Release();
                        return this;
                    }
                    _parent._reentrancy.Release();
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                var last = now;
                var remainder = timeout;

                // We need to wait for someone to leave the lock before trying again.
                while (remainder > TimeSpan.Zero)
                {
                    await _parent._reentrancy.WaitAsync(remainder).ConfigureAwait(false);
                    if (InnerTryEnter(synchronous: false))
                    {
                        // Reset the owning thread id after all await calls have finished, otherwise we
                        // could be resumed on a different thread and set an incorrect value.
                        _parent._owningThreadId = ThreadId;
                        _parent._reentrancy.Release();
                        return this;
                    }
                    _parent._reentrancy.Release();

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                    if (remainder < TimeSpan.Zero)
                    {
                        _parent._reentrancy.Release();
                        return null;
                    }

                    var waitTask = _parent._retry.WaitAsync(remainder).ConfigureAwait(false);
                    _parent._reentrancy.Release();
                    if (!await waitTask)
                    {
                        return null;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                }

                return null;
            }

            internal async Task<IDisposable?> TryObtainLockAsync(CancellationToken cancel)
            {
                try
                {
                    while (true)
                    {
                        await _parent._reentrancy.WaitAsync(cancel).ConfigureAwait(false);
                        if (InnerTryEnter(synchronous: false))
                        {
                            break;
                        }
                        // We need to wait for someone to leave the lock before trying again.
                        var waitTask = _parent._retry.WaitAsync(cancel).ConfigureAwait(false);
                        _parent._reentrancy.Release();
                        await waitTask;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                // Reset the owning thread id after all await calls have finished, otherwise we
                // could be resumed on a different thread and set an incorrect value.
                _parent._owningThreadId = ThreadId;
                _parent._reentrancy.Release();
                return this;
            }

            internal IDisposable ObtainLock(CancellationToken cancellationToken)
            {
                while (true)
                {
                    _parent._reentrancy.Wait(cancellationToken);
                    if (InnerTryEnter(synchronous: true))
                    {
                        _parent._reentrancy.Release();
                        break;
                    }
                    // We need to wait for someone to leave the lock before trying again.
                    var waitTask = _parent._retry.WaitAsync(cancellationToken);
                    _parent._reentrancy.Release();
                    // This should be safe since the task we are awaiting doesn't need to make progress
                    // itself to complete - it will be completed by another thread altogether. cf SemaphoreSlim internals.
                    waitTask.GetAwaiter().GetResult();
                }
                return this;
            }

            internal IDisposable? TryObtainLock(TimeSpan timeout)
            {
                // In case of zero-timeout, don't even wait for protective lock contention
                if (timeout == TimeSpan.Zero)
                {
                    _parent._reentrancy.Wait(timeout);
                    if (InnerTryEnter(synchronous: true))
                    {
                        _parent._reentrancy.Release();
                        return this;
                    }
                    _parent._reentrancy.Release();
                    return null;
                }

                var now = DateTimeOffset.UtcNow;
                var last = now;
                var remainder = timeout;

                // We need to wait for someone to leave the lock before trying again.
                while (remainder > TimeSpan.Zero)
                {
                    _parent._reentrancy.Wait(remainder);
                    if (InnerTryEnter(synchronous: true))
                    {
                        _parent._reentrancy.Release();
                        return this;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;

                    var waitTask = _parent._retry.WaitAsync(remainder);
                    _parent._reentrancy.Release();
                    if (!waitTask.GetAwaiter().GetResult())
                    {
                        return null;
                    }

                    now = DateTimeOffset.UtcNow;
                    remainder -= now - last;
                    last = now;
                }

                return null;
            }

            private bool InnerTryEnter(bool synchronous = false)
            {
                bool result = false;
                if (synchronous)
                {
                    if (_parent._owningThreadId == UnlockedId)
                    {
                        _parent._owningThreadId = ThreadId;
                    }
                    else if (_parent._owningThreadId != ThreadId)
                    {
                        return false;
                    }
                    _parent._owningId = AsyncLock.AsyncId;
                }
                else
                {
                    if (_parent._owningId == UnlockedId)
                    {
                        _parent._owningId = AsyncLock.AsyncId;
                    }
                    else if (_parent._owningId != _oldId)
                    {
                        // Another thread currently owns the lock
                        return false;
                    }
                    else
                    {
                        // Nested re-entrance
                        _parent._owningId = AsyncId;
                    }
                }

                // We can go in
                _parent._reentrances += 1;
                result = true;
                return result;
            }

            public void Dispose()
            {
#if DEBUG
                Debug.Assert(!_disposed);
                _disposed = true;
#endif
                var @this = this;
                var oldId = this._oldId;
                var oldThreadId = this._oldThreadId;
                @this._parent._reentrancy.Wait();
                try
                {
                    @this._parent._reentrances -= 1;
                    @this._parent._owningId = oldId;
                    @this._parent._owningThreadId = oldThreadId;
                    if (@this._parent._reentrances == 0)
                    {
                        // The owning thread is always the same so long as we
                        // are in a nested stack call. We reset the owning id
                        // only when the lock is fully unlocked.
                        @this._parent._owningId = UnlockedId;
                        @this._parent._owningThreadId = (int)UnlockedId;
                    }
                    // We can't place this within the _reentrances == 0 block above because we might
                    // still need to notify a parallel reentrant task to wake. I think.
                    // This should not be a race condition since we only wait on _retry with _reentrancy locked,
                    // then release _reentrancy so the Dispose() call can obtain it to signal _retry in a big hack.
                    if (@this._parent._retry.CurrentCount == 0)
                    {
                        @this._parent._retry.Release();
                    }
                }
                finally
                {
                    @this._parent._reentrancy.Release();
                }
            }
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<IDisposable> LockAsync(CancellationToken ct = default)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
            return @lock.ObtainLockAsync(ct);
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Action callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);

            return @lock.TryObtainLockAsync(timeout)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return false;
                    }

                    try
                    {
                        callback();
                    }
                    finally
                    {
                        disposableLock.Dispose();
                    }
                    return true;
                });
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Func<Task> callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);

            return @lock.TryObtainLockAsync(timeout)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return Task.FromResult(false);
                    }

                    return callback()
                        .ContinueWith(result =>
                        {
                            disposableLock.Dispose();

                            if (result.Exception is AggregateException ex)
                            {
                                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                            }

                            return true;
                        }, TaskScheduler.Default);
                }, TaskScheduler.Default).Unwrap();
        }

        // Make sure InnerLock.TryLockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Action callback, CancellationToken cancel)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);

            return @lock.TryObtainLockAsync(cancel)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return false;
                    }

                    try
                    {
                        callback();
                    }
                    finally
                    {
                        disposableLock.Dispose();
                    }
                    return true;
                }, TaskScheduler.Default);
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<bool> TryLockAsync(Func<Task> callback, CancellationToken cancel)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);

            return @lock.TryObtainLockAsync(cancel)
                .ContinueWith(state =>
                {
                    if (state.Exception is AggregateException ex)
                    {
                        ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                    }
                    var disposableLock = state.Result;
                    if (disposableLock is null)
                    {
                        return Task.FromResult(false);
                    }

                    return callback()
                        .ContinueWith(result =>
                        {
                            disposableLock.Dispose();

                            if (result.Exception is AggregateException ex)
                            {
                                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                            }

                            return true;
                        }, TaskScheduler.Default);
                }, TaskScheduler.Default).Unwrap();
        }

        public IDisposable Lock(CancellationToken cancellationToken = default)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            // Increment the async stack counter to prevent a child task from getting
            // the lock at the same time as a child thread.
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
            return @lock.ObtainLock(cancellationToken);
        }

        public bool TryLock(Action callback, TimeSpan timeout)
        {
            var @lock = new InnerLock(this, _asyncId.Value, ThreadId);
            // Increment the async stack counter to prevent a child task from getting
            // the lock at the same time as a child thread.
            _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
            var lockDisposable = @lock.TryObtainLock(timeout);
            if (lockDisposable is null)
            {
                return false;
            }

            // Execute the callback then release the lock
            try
            {
                callback();
            }
            finally
            {
                lockDisposable.Dispose();
            }
            return true;
        }
    }
}
