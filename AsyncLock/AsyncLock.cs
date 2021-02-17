using System;
using System.Diagnostics;
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
        private static long AsyncStackCounter = 0;
        // An AsyncLocal<T> is not really the task-based equivalent to a ThreadLocal<T>, in that
        // it does not track the async flow (as the documentation describes) but rather it is
        // associated with a stack snapshot. Mutation of the AsyncLocal in an await call does
        // not change the value observed by the parent when the call returns, so if you want to
        // use it as a persistent async flow identifier, the value needs to be set at the outer-
        // most level and never touched internally.
        private static readonly AsyncLocal<long> _asyncId = new AsyncLocal<long>();
        private static long AsyncId => _asyncId.Value;

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
#if DEBUG
            private bool _disposed;
#endif

            internal InnerLock(AsyncLock parent)
            {
                _parent = parent;
                _oldId = parent._owningId;
#if DEBUG
                _disposed = false;
#endif
            }

            internal async Task<IDisposable> ObtainLockAsync(CancellationToken ct = default)
            {
                while (!await TryEnterAsync())
                {
                    // We need to wait for someone to leave the lock before trying again.
                    await _parent._retry.WaitAsync(ct);
                }
                return this;
            }

            internal IDisposable ObtainLock()
            {
                while (!TryEnter())
                {
                    // We need to wait for someone to leave the lock before trying again.
                    _parent._retry.Wait();
                }
                return this;
            }

            private async Task<bool> TryEnterAsync()
            {
                await _parent._reentrancy.WaitAsync();
                return InnerTryEnter();
            }

            private bool TryEnter()
            {
                _parent._reentrancy.Wait();
                return InnerTryEnter();
            }

            private bool InnerTryEnter()
            {
                try
                {
                    Debug.Assert((_parent._owningId == UnlockedId) == (_parent._reentrances == 0));
                    if (_parent._owningId == UnlockedId)
                    {
                        // Obtain a new async stack ID
                        //_asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
                        _parent._owningId = AsyncLock.AsyncId;
                    }
                    else if (_parent._owningId != AsyncLock.AsyncId)
                    {
                        // Another thread currently owns the lock
                        return false;
                    }
                    else
                    {
                        // Nested re-entrance
                        _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
                        _parent._owningId = AsyncId;
                    }

                    // We can go in
                    Interlocked.Increment(ref _parent._reentrances);
                    return true;
                }
                finally
                {
                    _parent._reentrancy.Release();
                }
            }

            public void Dispose()
            {
#if DEBUG
                Debug.Assert(!_disposed);
                _disposed = true;
#endif
                var @this = this;
                var oldId = this._oldId;
                Task.Run(async () =>
                {
                    await @this._parent._reentrancy.WaitAsync();
                    try
                    {
                        Interlocked.Decrement(ref @this._parent._reentrances);
                        @this._parent._owningId = oldId;
                        if (@this._parent._reentrances == 0)
                        {
                            // The owning thread is always the same so long as we
                            // are in a nested stack call. We reset the owning id
                            // only when the lock is fully unlocked.
                            @this._parent._owningId = UnlockedId;
                            if (@this._parent._retry.CurrentCount == 0)
                            {
                                @this._parent._retry.Release();
                            }
                        }
                    }
                    finally
                    {
                        @this._parent._reentrancy.Release();
                    }
                });
            }
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<IDisposable> LockAsync()
        {
            return LockAsync(CancellationToken.None);
        }

        // Make sure InnerLock.LockAsync() does not use await, because an async function triggers a snapshot of
        // the AsyncLocal value.
        public Task<IDisposable> LockAsync(CancellationToken ct)
        {
            if (AsyncId == 0)
            {
                _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
            }
            var @lock = new InnerLock(this);
            return @lock.ObtainLockAsync(ct);
        }

        public IDisposable Lock()
        {
            if (AsyncId == 0)
            {
                _asyncId.Value = Interlocked.Increment(ref AsyncLock.AsyncStackCounter);
            }
            var @lock = new InnerLock(this);
            return @lock.ObtainLock();
        }
    }
}
