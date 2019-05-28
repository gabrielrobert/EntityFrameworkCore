// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Pipeline;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline
{
    public class RelationalCollectionShaperExpression : CollectionShaperExpression
    {
        public RelationalCollectionShaperExpression(
            ProjectionBindingExpression projection,
            Expression outerKeySelector,
            Expression innerKeySelector,
            Expression innerShaper,
            INavigation navigation)
            : base(projection, innerShaper, navigation)
        {
            OuterKeySelector = outerKeySelector;
            InnerKeySelector = innerKeySelector;
        }

        public Expression OuterKeySelector { get; }
        public Expression InnerKeySelector { get; }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var outerKeySelector = visitor.Visit(OuterKeySelector);
            var innerKeySelector = visitor.Visit(InnerKeySelector);
            var innerShaper = visitor.Visit(InnerShaper);

            return Update(outerKeySelector, innerKeySelector, innerShaper);
        }

        public RelationalCollectionShaperExpression Update(
            Expression outerKeySelector, Expression innerKeySelector, Expression innerShaper)
        {
            return outerKeySelector != OuterKeySelector || innerKeySelector != InnerKeySelector || innerShaper != InnerShaper
                ? new RelationalCollectionShaperExpression(Projection, outerKeySelector, innerKeySelector, innerShaper, Navigation)
                : this;
        }
    }
}
