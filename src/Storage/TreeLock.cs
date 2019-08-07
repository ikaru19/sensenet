﻿using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SenseNet.ContentRepository.Storage
{
    public class TreeLock : IDisposable
    {
        private readonly SnTrace.Operation _logOp;
        private readonly int[] _lockIds;

        private TreeLock(SnTrace.Operation logOp, params int[] lockIds)
        {
            this._logOp = logOp;
            _lockIds = lockIds;
        }

        /// <summary>
        /// Locks one or more subtrees in the Content Repository. If a subtree is locked, no modifications (Save operations) can be made there.
        /// Use this method with a using statement to make sure that the lock is released when not needed anymore.
        /// </summary>
        /// <exception cref="SenseNet.ContentRepository.Storage.LockedTreeException">Thrown when any of the requested paths (or any of the parent containers) are already locked.</exception>
        /// <param name="paths">One or more Content Repository paths to be locked.</param>
        public static TreeLock Acquire(params string[] paths)
        {
            SnTrace.ContentOperation.Write("TreeLock: Acquiring lock for {0}", paths);

            var lockIds = paths.Select(p =>  DataStore.AcquireTreeLockAsync(p, CancellationToken.None).Result).ToArray();
            for (var i = 0; i < lockIds.Length; i++)
            {
                if (lockIds[i] == 0)
                {
                    DataStore.ReleaseTreeLockAsync(lockIds, CancellationToken.None).Wait();
                    var msg = "Cannot acquire a tree lock for " + paths[i];
                    SnTrace.ContentOperation.Write("TreeLock: " + msg);
                    throw new LockedTreeException(msg);
                }
            }

            var logOp = SnTrace.ContentOperation.StartOperation("TreeLock: {0} for {1}", lockIds, paths);
            return new TreeLock(logOp, lockIds);
        }

        /// <summary>
        /// Checks whether a subtree is locked. Used by save operations to make sure that it is OK to make modifications.
        /// </summary>
        /// <exception cref="SenseNet.ContentRepository.Storage.LockedTreeException">Thrown when any of the requested paths (or any of the parent containers) are already locked.</exception>
        /// <param name="paths">One or more Content Repository paths to check for locked state.</param>
        public static void AssertFree(params string[] paths)
        {
            SnTrace.ContentOperation.Write("TreeLock: Checking {0}", String.Join(", ", paths));

            foreach (var path in paths)
            {
                if (DataStore.IsTreeLockedAsync(path, CancellationToken.None).Result)
                {
                    var msg = "Cannot perform the operation because another process is making changes on this path: " + path;
                    SnTrace.ContentOperation.Write("TreeLock: Checking {0}", String.Join(", ", paths));
                    throw new LockedTreeException(msg);
                }
            }
        }

        public void Dispose()
        {
            DataStore.ReleaseTreeLockAsync(_lockIds, CancellationToken.None).Wait();
            if (_logOp != null)
            {
                _logOp.Successful = true;
                _logOp.Dispose();
            }
        }

        public static Dictionary<int, string> GetAllLocks()
        {
            return DataStore.LoadAllTreeLocksAsync(CancellationToken.None).Result;
        }
    }
}
