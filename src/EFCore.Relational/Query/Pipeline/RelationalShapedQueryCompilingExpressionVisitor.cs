﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.NavigationExpansion;
using Microsoft.EntityFrameworkCore.Query.Pipeline;
using Microsoft.EntityFrameworkCore.Relational.Query.Pipeline.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline
{
    public partial class RelationalShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
    {
        private readonly IQuerySqlGeneratorFactory2 _querySqlGeneratorFactory;
        private readonly Type _contextType;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
        private static ParameterExpression _resultCoordinatorParameter
            = Expression.Parameter(typeof(ResultCoordinator), "resultCoordinator");

        public RelationalShapedQueryCompilingExpressionVisitor(
            IEntityMaterializerSource entityMaterializerSource,
            IQuerySqlGeneratorFactory2 querySqlGeneratorFactory,
            Type contextType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger,
            bool trackQueryResults,
            bool async)
            : base(entityMaterializerSource, trackQueryResults, async)
        {
            _querySqlGeneratorFactory = querySqlGeneratorFactory;
            _contextType = contextType;
            _logger = logger;
        }

        protected override Expression VisitShapedQueryExpression(ShapedQueryExpression shapedQueryExpression)
        {
            var shaperBody = InjectEntityMaterializer(shapedQueryExpression.ShaperExpression);

            var selectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;

            shaperBody = new RelationalProjectionBindingRemovingExpressionVisitor(selectExpression).Visit(shaperBody);

            shaperBody = new IncludeCompilingExpressionVisitor(TrackQueryResults).Visit(shaperBody);

            var shaperLambda = Expression.Lambda(
                shaperBody,
                QueryCompilationContext2.QueryContextParameter,
                RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter,
                _resultCoordinatorParameter);

            if (Async)
            {
                return Expression.New(
                    typeof(AsyncQueryingEnumerable<>).MakeGenericType(shaperLambda.ReturnType.GetGenericArguments().Single()).GetConstructors()[0],
                    Expression.Convert(QueryCompilationContext2.QueryContextParameter, typeof(RelationalQueryContext)),
                    Expression.Constant(_querySqlGeneratorFactory.Create()),
                    Expression.Constant(selectExpression),
                    Expression.Constant(shaperLambda.Compile()),
                    Expression.Constant(_contextType),
                    Expression.Constant(_logger));
            }

            return Expression.New(
                typeof(QueryingEnumerable<>).MakeGenericType(shaperLambda.ReturnType).GetConstructors()[0],
                Expression.Convert(QueryCompilationContext2.QueryContextParameter, typeof(RelationalQueryContext)),
                Expression.Constant(_querySqlGeneratorFactory.Create()),
                Expression.Constant(selectExpression),
                Expression.Constant(shaperLambda.Compile()),
                Expression.Constant(_contextType),
                Expression.Constant(_logger));
        }

        private class ResultCoordinator
        {
            public bool? HasNext { get; set; }
            public object[] KeyValues { get; set; }
        }

        private class IncludeCompilingExpressionVisitor : ExpressionVisitor
        {
            private readonly bool _tracking;

            public IncludeCompilingExpressionVisitor(bool tracking)
            {
                _tracking = tracking;
            }

            private static readonly MethodInfo _includeReferenceMethodInfo
                = typeof(IncludeCompilingExpressionVisitor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IncludeReference));

            private static void IncludeReference<TEntity, TIncludedEntity>(
                QueryContext queryContext,
                DbDataReader dbDataReader,
                TEntity entity,
                Func<QueryContext, DbDataReader, ResultCoordinator, TIncludedEntity> innerShaper,
                INavigation navigation,
                INavigation inverseNavigation,
                Action<TEntity, TIncludedEntity> fixup,
                bool trackingQuery,
                ResultCoordinator resultCoordinator)
            {
                var relatedEntity = innerShaper(queryContext, dbDataReader, resultCoordinator);

                if (!trackingQuery)
                {
                    SetIsLoadedNoTracking(entity, navigation);
                    if (!ReferenceEquals(relatedEntity, null))
                    {
                        fixup(entity, relatedEntity);
                        if (inverseNavigation != null && !inverseNavigation.IsCollection())
                        {
                            SetIsLoadedNoTracking(relatedEntity, inverseNavigation);
                        }
                    }
                }
            }

            private static readonly MethodInfo _includeCollectionMethodInfo
                = typeof(IncludeCompilingExpressionVisitor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IncludeCollection));

            private static void IncludeCollection<TEntity, TIncludedEntity>(
                QueryContext queryContext,
                DbDataReader dbDataReader,
                TEntity entity,
                Func<QueryContext, DbDataReader, object[]> outerKeySelector,
                Func<QueryContext, DbDataReader, object[]> innerKeySelector,
                Func<QueryContext, DbDataReader, ResultCoordinator, TIncludedEntity> innerShaper,
                INavigation navigation,
                INavigation inverseNavigation,
                Action<TEntity, TIncludedEntity> fixup,
                bool trackingQuery,
                ResultCoordinator resultCoordinator)
            {
                if (entity is null)
                {
                    return;
                }

                if (trackingQuery)
                {
                    queryContext.StateManager.TryGetEntry(entity).SetIsLoaded(navigation);
                }
                else
                {
                    SetIsLoadedNoTracking(entity, navigation);
                }


                var innerKey = innerKeySelector(queryContext, dbDataReader);
                var outerKey = outerKeySelector(queryContext, dbDataReader);

                resultCoordinator.HasNext = null;
                var relatedEntity = innerShaper(queryContext, dbDataReader, resultCoordinator);

                if (ReferenceEquals(relatedEntity, null))
                {
                    navigation.GetCollectionAccessor().GetOrCreate(entity);
                    return;
                }

                if (!trackingQuery)
                {
                    fixup(entity, relatedEntity);
                    if (inverseNavigation != null && !inverseNavigation.IsCollection())
                    {
                        SetIsLoadedNoTracking(relatedEntity, inverseNavigation);
                    }
                }

                if (resultCoordinator.HasNext == false)
                {
                    return;
                }

                while (resultCoordinator.HasNext ?? dbDataReader.Read())
                {
                    resultCoordinator.HasNext = null;
                    var currentOuterKey = outerKeySelector(queryContext, dbDataReader);
                    if (!StructuralComparisons.StructuralEqualityComparer.Equals(outerKey, currentOuterKey))
                    {
                        resultCoordinator.HasNext = true;
                        break;
                    }

                    var currentInnerKey = innerKeySelector(queryContext, dbDataReader);
                    if (StructuralComparisons.StructuralEqualityComparer.Equals(innerKey, currentInnerKey))
                    {
                        continue;
                    }

                    relatedEntity = innerShaper(queryContext, dbDataReader, resultCoordinator);
                    if (!trackingQuery)
                    {
                        fixup(entity, relatedEntity);
                        if (inverseNavigation != null && !inverseNavigation.IsCollection())
                        {
                            SetIsLoadedNoTracking(relatedEntity, inverseNavigation);
                        }
                    }

                    if (resultCoordinator.HasNext == false)
                    {
                        return;
                    }
                }
            }

            private static void SetIsLoadedNoTracking(object entity, INavigation navigation)
            => ((ILazyLoader)((PropertyBase)navigation
                        .DeclaringEntityType
                        .GetServiceProperties()
                        .FirstOrDefault(p => p.ClrType == typeof(ILazyLoader)))
                    ?.Getter.GetClrValue(entity))
                ?.SetLoaded(entity, navigation.Name);


            protected override Expression VisitExtension(Expression extensionExpression)
            {
                if (extensionExpression is IncludeExpression includeExpression)
                {
                    if (includeExpression.Navigation.IsCollection())
                    {
                        var entityClrType = includeExpression.EntityExpression.Type;
                        var relatedEntityClrType = includeExpression.NavigationExpression.Type.TryGetSequenceType();
                        var inverseNavigation = includeExpression.Navigation.FindInverse();
                        var collectionShaper = (RelationalCollectionShaperExpression)includeExpression.NavigationExpression;
                        var innerShaper = Visit(collectionShaper.InnerShaper);
                        return Expression.Call(
                            _includeCollectionMethodInfo.MakeGenericMethod(entityClrType, relatedEntityClrType),
                            QueryCompilationContext2.QueryContextParameter,
                            RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter,
                            // We don't need to visit entityExpression since it is supposed to be a parameterExpression only
                            includeExpression.EntityExpression,
                            Expression.Constant(
                                Expression.Lambda(
                                    collectionShaper.OuterKeySelector,
                                    QueryCompilationContext2.QueryContextParameter,
                                    RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter).Compile()),
                            Expression.Constant(
                                Expression.Lambda(
                                    collectionShaper.InnerKeySelector,
                                    QueryCompilationContext2.QueryContextParameter,
                                    RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter).Compile()),
                            Expression.Constant(
                                Expression.Lambda(
                                    innerShaper,
                                    QueryCompilationContext2.QueryContextParameter,
                                    RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter,
                                    _resultCoordinatorParameter).Compile()),
                            Expression.Constant(includeExpression.Navigation),
                            Expression.Constant(inverseNavigation, typeof(INavigation)),
                            Expression.Constant(
                                GenerateFixup(entityClrType, relatedEntityClrType, includeExpression.Navigation, inverseNavigation).Compile()),
                            Expression.Constant(_tracking),
                            _resultCoordinatorParameter);
                    }
                    else
                    {
                        var entityClrType = includeExpression.EntityExpression.Type;
                        var relatedEntityClrType = includeExpression.NavigationExpression.Type;
                        var inverseNavigation = includeExpression.Navigation.FindInverse();
                        return Expression.Call(
                            _includeReferenceMethodInfo.MakeGenericMethod(entityClrType, relatedEntityClrType),
                            QueryCompilationContext2.QueryContextParameter,
                            RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter,
                            // We don't need to visit entityExpression since it is supposed to be a parameterExpression only
                            includeExpression.EntityExpression,
                            Expression.Constant(
                                Expression.Lambda(
                                    Visit(includeExpression.NavigationExpression),
                                    QueryCompilationContext2.QueryContextParameter,
                                    RelationalProjectionBindingRemovingExpressionVisitor.DataReaderParameter,
                                    _resultCoordinatorParameter).Compile()),
                            Expression.Constant(includeExpression.Navigation),
                            Expression.Constant(inverseNavigation, typeof(INavigation)),
                            Expression.Constant(
                                GenerateFixup(entityClrType, relatedEntityClrType, includeExpression.Navigation, inverseNavigation).Compile()),
                            Expression.Constant(_tracking),
                            _resultCoordinatorParameter);
                    }
                }

                return base.VisitExtension(extensionExpression);
            }

            private static LambdaExpression GenerateFixup(
                Type entityType,
                Type relatedEntityType,
                INavigation navigation,
                INavigation inverseNavigation)
            {
                var entityParameter = Expression.Parameter(entityType);
                var relatedEntityParameter = Expression.Parameter(relatedEntityType);
                var expressions = new List<Expression>
                {
                    navigation.IsCollection()
                        ? AddToCollectionNavigation(entityParameter, relatedEntityParameter, navigation)
                        : AssignReferenceNavigation(entityParameter, relatedEntityParameter, navigation)
                };

                if (inverseNavigation != null)
                {
                    expressions.Add(
                        inverseNavigation.IsCollection()
                            ? AddToCollectionNavigation(relatedEntityParameter, entityParameter, inverseNavigation)
                            : AssignReferenceNavigation(relatedEntityParameter, entityParameter, inverseNavigation));

                }

                return Expression.Lambda(Expression.Block(typeof(void), expressions), entityParameter, relatedEntityParameter);
            }

            private static Expression AssignReferenceNavigation(
                ParameterExpression entity,
                ParameterExpression relatedEntity,
                INavigation navigation)
            {
                return entity.MakeMemberAccess(navigation.GetMemberInfo(forConstruction: false, forSet: true))
                    .CreateAssignExpression(relatedEntity);
            }

            private static Expression AddToCollectionNavigation(
                ParameterExpression entity,
                ParameterExpression relatedEntity,
                INavigation navigation)
            {
                return Expression.Call(
                    Expression.Constant(navigation.GetCollectionAccessor()),
                    _collectionAccessorAddMethodInfo,
                    entity,
                    relatedEntity);
            }

            private static readonly MethodInfo _collectionAccessorAddMethodInfo
                = typeof(IClrCollectionAccessor).GetTypeInfo()
                    .GetDeclaredMethod(nameof(IClrCollectionAccessor.Add));
        }

        private class AsyncQueryingEnumerable<T> : IAsyncEnumerable<T>
        {
            private readonly RelationalQueryContext _relationalQueryContext;
            private readonly SelectExpression _selectExpression;
            private readonly Func<QueryContext, DbDataReader, ResultCoordinator, Task<T>> _shaper;
            private readonly QuerySqlGenerator _querySqlGenerator;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

            public AsyncQueryingEnumerable(
                RelationalQueryContext relationalQueryContext,
                QuerySqlGenerator querySqlGenerator,
                SelectExpression selectExpression,
                Func<QueryContext, DbDataReader, ResultCoordinator, Task<T>> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            {
                _relationalQueryContext = relationalQueryContext;
                _querySqlGenerator = querySqlGenerator;
                _selectExpression = selectExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
            }

            public IAsyncEnumerator<T> GetEnumerator()
            {
                return new AsyncEnumerator(this);
            }

            private sealed class AsyncEnumerator : IAsyncEnumerator<T>
            {
                private RelationalDataReader _dataReader;
                private ResultCoordinator _resultCoordinator;
                private readonly RelationalQueryContext _relationalQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, DbDataReader, ResultCoordinator, Task<T>> _shaper;
                private readonly QuerySqlGenerator _querySqlGenerator;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

                public AsyncEnumerator(AsyncQueryingEnumerable<T> queryingEnumerable)
                {
                    _relationalQueryContext = queryingEnumerable._relationalQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _querySqlGenerator = queryingEnumerable._querySqlGenerator;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                }

                public T Current { get; private set; }

                public void Dispose()
                {
                    _dataReader?.Dispose();
                    _dataReader = null;
                    _relationalQueryContext.Connection.Close();
                }

                public async Task<bool> MoveNext(CancellationToken cancellationToken)
                {
                    if (_dataReader == null)
                    {
                        await _relationalQueryContext.Connection.OpenAsync(cancellationToken);

                        try
                        {
                            var relationalCommand = _querySqlGenerator
                                .GetCommand(
                                    _selectExpression,
                                    _relationalQueryContext.ParameterValues,
                                    _relationalQueryContext.CommandLogger);

                            _dataReader
                                = await relationalCommand.ExecuteReaderAsync(
                                    _relationalQueryContext.Connection,
                                    _relationalQueryContext.ParameterValues,
                                    _relationalQueryContext.CommandLogger,
                                    cancellationToken);

                            _resultCoordinator = new ResultCoordinator();
                        }
                        catch (Exception exception)
                        {
                            _logger.QueryIterationFailed(_contextType, exception);
                            // If failure happens creating the data reader, then it won't be available to
                            // handle closing the connection, so do it explicitly here to preserve ref counting.
                            _relationalQueryContext.Connection.Close();

                            throw;
                        }
                    }

                    try
                    {
                        if (_resultCoordinator.HasNext == false)
                        {
                            Current = default;

                            return false;
                        }

                        var hasNext = _resultCoordinator.HasNext ?? await _dataReader.ReadAsync(cancellationToken);

                        _resultCoordinator.HasNext = null;

                        Current
                            = hasNext
                                ? await _shaper(_relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator)
                                : default;

                        return hasNext;
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }
            }
        }

        private class QueryingEnumerable<T> : IEnumerable<T>
        {
            private readonly RelationalQueryContext _relationalQueryContext;
            private readonly SelectExpression _selectExpression;
            private readonly Func<QueryContext, DbDataReader, ResultCoordinator, T> _shaper;
            private readonly QuerySqlGenerator _querySqlGenerator;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

            public QueryingEnumerable(RelationalQueryContext relationalQueryContext,
                QuerySqlGenerator querySqlGenerator,
                SelectExpression selectExpression,
                Func<QueryContext, DbDataReader, ResultCoordinator, T> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            {
                _relationalQueryContext = relationalQueryContext;
                _querySqlGenerator = querySqlGenerator;
                _selectExpression = selectExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
            }

            public IEnumerator<T> GetEnumerator() => new Enumerator(this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<T>
            {
                private RelationalDataReader _dataReader;
                private ResultCoordinator _resultCoordinator;
                private readonly RelationalQueryContext _relationalQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, DbDataReader, ResultCoordinator, T> _shaper;
                private readonly QuerySqlGenerator _querySqlGenerator;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

                public Enumerator(QueryingEnumerable<T> queryingEnumerable)
                {
                    _relationalQueryContext = queryingEnumerable._relationalQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _querySqlGenerator = queryingEnumerable._querySqlGenerator;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                }

                public T Current { get; private set; }

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                    _dataReader?.Dispose();
                    _dataReader = null;
                    _relationalQueryContext.Connection.Close();
                }

                public bool MoveNext()
                {
                    if (_dataReader == null)
                    {
                        _relationalQueryContext.Connection.Open();

                        try
                        {
                            var relationalCommand = _querySqlGenerator
                                .GetCommand(
                                    _selectExpression,
                                    _relationalQueryContext.ParameterValues,
                                    _relationalQueryContext.CommandLogger);

                            _dataReader
                                = relationalCommand.ExecuteReader(
                                    _relationalQueryContext.Connection,
                                    _relationalQueryContext.ParameterValues,
                                    _relationalQueryContext.CommandLogger);

                            _resultCoordinator = new ResultCoordinator();
                        }
                        catch (Exception exception)
                        {
                            _logger.QueryIterationFailed(_contextType, exception);
                            // If failure happens creating the data reader, then it won't be available to
                            // handle closing the connection, so do it explicitly here to preserve ref counting.
                            _relationalQueryContext.Connection.Close();

                            throw;
                        }
                    }

                    try
                    {
                        if (_resultCoordinator.HasNext == false)
                        {
                            Current = default;

                            return false;
                        }

                        var hasNext = _resultCoordinator.HasNext ?? _dataReader.Read();

                        _resultCoordinator.HasNext = null;

                        Current
                            = hasNext
                                ? _shaper(_relationalQueryContext, _dataReader.DbDataReader, _resultCoordinator)
                                : default;

                        return hasNext;
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public void Reset() => throw new NotImplementedException();
            }
        }

        private class RelationalProjectionBindingRemovingExpressionVisitor : ExpressionVisitor
        {
            public static readonly ParameterExpression DataReaderParameter
                = Expression.Parameter(typeof(DbDataReader), "dataReader");

            private readonly IDictionary<ParameterExpression, IDictionary<IProperty, int>> _materializationContextBindings
                = new Dictionary<ParameterExpression, IDictionary<IProperty, int>>();

            public RelationalProjectionBindingRemovingExpressionVisitor(SelectExpression selectExpression)
            {
                _selectExpression = selectExpression;
            }

            protected override Expression VisitBinary(BinaryExpression binaryExpression)
            {
                if (binaryExpression.NodeType == ExpressionType.Assign
                    && binaryExpression.Left is ParameterExpression parameterExpression
                    && parameterExpression.Type == typeof(MaterializationContext))
                {
                    var newExpression = (NewExpression)binaryExpression.Right;
                    var projectionBindingExpression = (ProjectionBindingExpression)newExpression.Arguments[0];

                    _materializationContextBindings[parameterExpression]
                        = (IDictionary<IProperty, int>)GetProjectionIndex(projectionBindingExpression);

                    var updatedExpression = Expression.New(newExpression.Constructor,
                        Expression.Constant(ValueBuffer.Empty),
                        newExpression.Arguments[1]);

                    return Expression.MakeBinary(ExpressionType.Assign, binaryExpression.Left, updatedExpression);
                }

                return base.VisitBinary(binaryExpression);
            }

            protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
            {
                if (methodCallExpression.Method.IsGenericMethod
                    && methodCallExpression.Method.GetGenericMethodDefinition() == EntityMaterializerSource.TryReadValueMethod)
                {
                    var property = (IProperty)((ConstantExpression)methodCallExpression.Arguments[2]).Value;
                    var propertyProjectionMap = methodCallExpression.Arguments[0] is ProjectionBindingExpression projectionBindingExpression
                        ? (IDictionary<IProperty, int>)GetProjectionIndex(projectionBindingExpression)
                        : _materializationContextBindings[(ParameterExpression)((MethodCallExpression)methodCallExpression.Arguments[0]).Object];

                    var projectionIndex = propertyProjectionMap[property];
                    var projection = _selectExpression.Projection[projectionIndex];

                    return CreateGetValueExpression(
                        projectionIndex,
                        IsNullableProjection(projection),
                        property.FindRelationalMapping(),
                        methodCallExpression.Type);
                }

                return base.VisitMethodCall(methodCallExpression);
            }

            protected override Expression VisitExtension(Expression extensionExpression)
            {
                if (extensionExpression is ProjectionBindingExpression projectionBindingExpression)
                {
                    var projectionIndex = (int)GetProjectionIndex(projectionBindingExpression);
                    var projection = _selectExpression.Projection[projectionIndex];

                    return CreateGetValueExpression(
                        projectionIndex,
                        IsNullableProjection(projection),
                        projection.Expression.TypeMapping,
                        projectionBindingExpression.Type);
                }

                if (extensionExpression is RelationalCollectionShaperExpression relationalCollectionShaperExpression)
                {
                    return new RelationalCollectionShaperExpression(
                        relationalCollectionShaperExpression.Projection,
                        Visit(relationalCollectionShaperExpression.OuterKeySelector),
                        Visit(relationalCollectionShaperExpression.InnerKeySelector),
                        Visit(relationalCollectionShaperExpression.InnerShaper),
                        relationalCollectionShaperExpression.Navigation);
                }

                return base.VisitExtension(extensionExpression);
            }

            private object GetProjectionIndex(ProjectionBindingExpression projectionBindingExpression)
            {
                return projectionBindingExpression.ProjectionMember != null
                    ? ((ConstantExpression)_selectExpression.GetProjectionExpression(projectionBindingExpression.ProjectionMember)).Value
                    : (projectionBindingExpression.Index != null
                        ? (object)projectionBindingExpression.Index
                        : projectionBindingExpression.IndexMap);
            }

            private static bool IsNullableProjection(ProjectionExpression projection)
            {
                return projection.Expression is ColumnExpression column ? column.Nullable : true;
            }

            private static Expression CreateGetValueExpression(
                int index,
                bool nullable,
                RelationalTypeMapping typeMapping,
                Type clrType)
            {
                var getMethod = typeMapping.GetDataReaderMethod();

                var indexExpression = Expression.Constant(index);

                Expression valueExpression
                    = Expression.Call(
                        getMethod.DeclaringType != typeof(DbDataReader)
                            ? Expression.Convert(DataReaderParameter, getMethod.DeclaringType)
                            : (Expression)DataReaderParameter,
                        getMethod,
                        indexExpression);

                valueExpression = typeMapping.CustomizeDataReaderExpression(valueExpression);

                var converter = typeMapping.Converter;

                if (converter != null)
                {
                    if (valueExpression.Type != converter.ProviderClrType)
                    {
                        valueExpression = Expression.Convert(valueExpression, converter.ProviderClrType);
                    }

                    valueExpression = ReplacingExpressionVisitor.Replace(
                        converter.ConvertFromProviderExpression.Parameters.Single(),
                        valueExpression,
                        converter.ConvertFromProviderExpression.Body);
                }

                if (valueExpression.Type != clrType)
                {
                    valueExpression = Expression.Convert(valueExpression, clrType);
                }

                //var exceptionParameter
                //    = Expression.Parameter(typeof(Exception), name: "e");

                //var property = materializationInfo.Property;

                //if (detailedErrorsEnabled)
                //{
                //    var catchBlock
                //        = Expression
                //            .Catch(
                //                exceptionParameter,
                //                Expression.Call(
                //                    _throwReadValueExceptionMethod
                //                        .MakeGenericMethod(valueExpression.Type),
                //                    exceptionParameter,
                //                    Expression.Call(
                //                        dataReaderExpression,
                //                        _getFieldValueMethod.MakeGenericMethod(typeof(object)),
                //                        indexExpression),
                //                    Expression.Constant(property, typeof(IPropertyBase))));

                //    valueExpression = Expression.TryCatch(valueExpression, catchBlock);
                //}

                //if (box && valueExpression.Type.GetTypeInfo().IsValueType)
                //{
                //    valueExpression = Expression.Convert(valueExpression, typeof(object));
                //}

                if (nullable)
                {
                    valueExpression
                        = Expression.Condition(
                            Expression.Call(DataReaderParameter, _isDbNullMethod, indexExpression),
                            Expression.Default(valueExpression.Type),
                            valueExpression);
                }

                return valueExpression;
            }

            private static readonly MethodInfo _isDbNullMethod =
                typeof(DbDataReader).GetRuntimeMethod(nameof(DbDataReader.IsDBNull), new[] { typeof(int) });

            private readonly SelectExpression _selectExpression;
        }
    }
}
