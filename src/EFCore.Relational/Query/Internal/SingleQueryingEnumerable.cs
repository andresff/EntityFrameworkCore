// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class SingleQueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>, IRelationalQueryingEnumerable
    {
        private readonly RelationalQueryContext _relationalQueryContext;
        private readonly RelationalCommandCache _relationalCommandCache;
        private readonly Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> _shaper;
        private readonly Type _contextType;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
        private readonly bool _standAloneStateManager;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public SingleQueryingEnumerable(
            [NotNull] RelationalQueryContext relationalQueryContext,
            [NotNull] RelationalCommandCache relationalCommandCache,
            [NotNull] Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> shaper,
            [NotNull] Type contextType,
            bool standAloneStateManager)
        {
            _relationalQueryContext = relationalQueryContext;
            _relationalCommandCache = relationalCommandCache;
            _shaper = shaper;
            _contextType = contextType;
            _queryLogger = relationalQueryContext.QueryLogger;
            _standAloneStateManager = standAloneStateManager;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new AsyncEnumerator(this, cancellationToken);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IEnumerator<T> GetEnumerator() => new Enumerator(this);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DbCommand CreateDbCommand()
            => _relationalCommandCache
                .GetRelationalCommand(_relationalQueryContext.ParameterValues)
                .CreateDbCommand(
                    new RelationalCommandParameterObject(
                        _relationalQueryContext.Connection,
                        _relationalQueryContext.ParameterValues,
                        null,
                        null,
                        null),
                    Guid.Empty,
                    (DbCommandMethod)(-1));

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual string ToQueryString()
            => _relationalQueryContext.RelationalQueryStringFactory.Create(CreateDbCommand());

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly RelationalQueryContext _relationalQueryContext;
            private readonly RelationalCommandCache _relationalCommandCache;
            private readonly Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> _shaper;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
            private readonly bool _standAloneStateManager;

            private RelationalDataReader _dataReader;
            private SingleQueryResultCoordinator _resultCoordinator;
            private IExecutionStrategy _executionStrategy;

            public Enumerator(SingleQueryingEnumerable<T> queryingEnumerable)
            {
                _relationalQueryContext = queryingEnumerable._relationalQueryContext;
                _relationalCommandCache = queryingEnumerable._relationalCommandCache;
                _shaper = queryingEnumerable._shaper;
                _contextType = queryingEnumerable._contextType;
                _queryLogger = queryingEnumerable._queryLogger;
                _standAloneStateManager = queryingEnumerable._standAloneStateManager;
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                try
                {
                    using (_relationalQueryContext.ConcurrencyDetector.EnterCriticalSection())
                    {
                        if (_dataReader == null)
                        {
                            if (_executionStrategy == null)
                            {
                                _executionStrategy = _relationalQueryContext.ExecutionStrategyFactory.Create();
                            }

                            _executionStrategy.Execute(true, InitializeReader, null);
                        }

                        var hasNext = _resultCoordinator.HasNext ?? _dataReader.Read();
                        Current = default;

                        if (hasNext)
                        {
                            while (true)
                            {
                                _resultCoordinator.ResultReady = true;
                                _resultCoordinator.HasNext = null;
                                Current = _shaper(
                                    _relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator.ResultContext, _resultCoordinator);
                                if (_resultCoordinator.ResultReady)
                                {
                                    // We generated a result so null out previously stored values
                                    _resultCoordinator.ResultContext.Values = null;
                                    break;
                                }

                                if (!_dataReader.Read())
                                {
                                    _resultCoordinator.HasNext = false;
                                    // Enumeration has ended, materialize last element
                                    _resultCoordinator.ResultReady = true;
                                    Current = _shaper(
                                        _relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator.ResultContext, _resultCoordinator);

                                    break;
                                }
                            }
                        }

                        return hasNext;
                    }
                }
                catch (Exception exception)
                {
                    _queryLogger.QueryIterationFailed(_contextType, exception);

                    throw;
                }
            }

            private bool InitializeReader(DbContext _, bool result)
            {
                var relationalCommand = _relationalCommandCache.GetRelationalCommand(_relationalQueryContext.ParameterValues);

                _dataReader
                    = relationalCommand.ExecuteReader(
                        new RelationalCommandParameterObject(
                            _relationalQueryContext.Connection,
                            _relationalQueryContext.ParameterValues,
                            _relationalCommandCache.ReaderColumns,
                            _relationalQueryContext.Context,
                            _relationalQueryContext.CommandLogger));

                _resultCoordinator = new SingleQueryResultCoordinator();

                _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

                return result;
            }

            public void Dispose()
            {
                _dataReader?.Dispose();
                _dataReader = null;
            }

            public void Reset() => throw new NotImplementedException();
        }

        private sealed class AsyncEnumerator : IAsyncEnumerator<T>
        {
            private readonly RelationalQueryContext _relationalQueryContext;
            private readonly RelationalCommandCache _relationalCommandCache;
            private readonly Func<QueryContext, DbDataReader, ResultContext, SingleQueryResultCoordinator, T> _shaper;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
            private readonly bool _standAloneStateManager;
            private readonly CancellationToken _cancellationToken;

            private RelationalDataReader _dataReader;
            private SingleQueryResultCoordinator _resultCoordinator;
            private IExecutionStrategy _executionStrategy;

            public AsyncEnumerator(
                SingleQueryingEnumerable<T> queryingEnumerable,
                CancellationToken cancellationToken)
            {
                _relationalQueryContext = queryingEnumerable._relationalQueryContext;
                _relationalCommandCache = queryingEnumerable._relationalCommandCache;
                _shaper = queryingEnumerable._shaper;
                _contextType = queryingEnumerable._contextType;
                _queryLogger = queryingEnumerable._queryLogger;
                _standAloneStateManager = queryingEnumerable._standAloneStateManager;
                _cancellationToken = cancellationToken;
            }

            public T Current { get; private set; }

            public async ValueTask<bool> MoveNextAsync()
            {
                try
                {
                    using (_relationalQueryContext.ConcurrencyDetector.EnterCriticalSection())
                    {
                        if (_dataReader == null)
                        {
                            if (_executionStrategy == null)
                            {
                                _executionStrategy = _relationalQueryContext.ExecutionStrategyFactory.Create();
                            }

                            await _executionStrategy.ExecuteAsync(true, InitializeReaderAsync, null, _cancellationToken)
                                .ConfigureAwait(false);
                        }

                        var hasNext = _resultCoordinator.HasNext ?? await _dataReader.ReadAsync(_cancellationToken).ConfigureAwait(false);
                        Current = default;

                        if (hasNext)
                        {
                            while (true)
                            {
                                _resultCoordinator.ResultReady = true;
                                _resultCoordinator.HasNext = null;
                                Current = _shaper(
                                    _relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator.ResultContext, _resultCoordinator);
                                if (_resultCoordinator.ResultReady)
                                {
                                    // We generated a result so null out previously stored values
                                    _resultCoordinator.ResultContext.Values = null;
                                    break;
                                }

                                if (!await _dataReader.ReadAsync(_cancellationToken).ConfigureAwait(false))
                                {
                                    _resultCoordinator.HasNext = false;
                                    // Enumeration has ended, materialize last element
                                    _resultCoordinator.ResultReady = true;
                                    Current = _shaper(
                                        _relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator.ResultContext, _resultCoordinator);

                                    break;
                                }
                            }
                        }

                        return hasNext;
                    }
                }
                catch (Exception exception)
                {
                    _queryLogger.QueryIterationFailed(_contextType, exception);

                    throw;
                }
            }

            private async Task<bool> InitializeReaderAsync(DbContext _, bool result, CancellationToken cancellationToken)
            {
                var relationalCommand = _relationalCommandCache.GetRelationalCommand(_relationalQueryContext.ParameterValues);

                _dataReader
                    = await relationalCommand.ExecuteReaderAsync(
                        new RelationalCommandParameterObject(
                            _relationalQueryContext.Connection,
                            _relationalQueryContext.ParameterValues,
                            _relationalCommandCache.ReaderColumns,
                            _relationalQueryContext.Context,
                            _relationalQueryContext.CommandLogger),
                        cancellationToken)
                        .ConfigureAwait(false);

                _resultCoordinator = new SingleQueryResultCoordinator();

                _relationalQueryContext.InitializeStateManager(_standAloneStateManager);

                return result;
            }

            public ValueTask DisposeAsync()
            {
                if (_dataReader != null)
                {
                    var dataReader = _dataReader;
                    _dataReader = null;

                    return dataReader.DisposeAsync();
                }

                return default;
            }
        }
    }
}
