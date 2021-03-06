// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions
{
    /// <summary>
    ///     Represents a set of conventions used to build a model.
    /// </summary>
    public class ConventionSet
    {
        /// <summary>
        ///     Conventions to run to setup the initial model.
        /// </summary>
        public virtual IList<IModelInitializedConvention> ModelInitializedConventions { get; } = new List<IModelInitializedConvention>();

        /// <summary>
        ///     Conventions to run when model building is completed.
        /// </summary>
        public virtual IList<IModelFinalizedConvention> ModelFinalizedConventions { get; } = new List<IModelFinalizedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is set or removed on a model.
        /// </summary>
        public virtual IList<IModelAnnotationChangedConvention> ModelAnnotationChangedConventions { get; }
            = new List<IModelAnnotationChangedConvention>();

        /// <summary>
        ///     Conventions to run when an entity type is added to the model.
        /// </summary>
        public virtual IList<IEntityTypeAddedConvention> EntityTypeAddedConventions { get; } = new List<IEntityTypeAddedConvention>();

        /// <summary>
        ///     Conventions to run when an entity type is ignored.
        /// </summary>
        public virtual IList<IEntityTypeIgnoredConvention> EntityTypeIgnoredConventions { get; } = new List<IEntityTypeIgnoredConvention>();

        /// <summary>
        ///     Conventions to run when an entity type is removed.
        /// </summary>
        public virtual IList<IEntityTypeRemovedConvention> EntityTypeRemovedConventions { get; } = new List<IEntityTypeRemovedConvention>();

        /// <summary>
        ///     Conventions to run when a property is ignored.
        /// </summary>
        public virtual IList<IEntityTypeMemberIgnoredConvention> EntityTypeMemberIgnoredConventions { get; }
            = new List<IEntityTypeMemberIgnoredConvention>();

        /// <summary>
        ///     Conventions to run when the base entity type is changed.
        /// </summary>
        public virtual IList<IEntityTypeBaseTypeChangedConvention> EntityTypeBaseTypeChangedConventions { get; }
            = new List<IEntityTypeBaseTypeChangedConvention>();

        /// <summary>
        ///     Conventions to run when a primary key is changed.
        /// </summary>
        public virtual IList<IEntityTypePrimaryKeyChangedConvention> EntityTypePrimaryKeyChangedConventions { get; }
            = new List<IEntityTypePrimaryKeyChangedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is set or removed on an entity type.
        /// </summary>
        public virtual IList<IEntityTypeAnnotationChangedConvention> EntityTypeAnnotationChangedConventions { get; }
            = new List<IEntityTypeAnnotationChangedConvention>();

        /// <summary>
        ///     Conventions to run when a foreign key is added.
        /// </summary>
        public virtual IList<IForeignKeyAddedConvention> ForeignKeyAddedConventions { get; } = new List<IForeignKeyAddedConvention>();

        /// <summary>
        ///     Conventions to run when a foreign key is removed.
        /// </summary>
        public virtual IList<IForeignKeyRemovedConvention> ForeignKeyRemovedConventions { get; } = new List<IForeignKeyRemovedConvention>();

        /// <summary>
        ///     Conventions to run when the principal end of a relationship is configured.
        /// </summary>
        public virtual IList<IForeignKeyPrincipalEndChangedConvention> ForeignKeyPrincipalEndChangedConventions { get; }
            = new List<IForeignKeyPrincipalEndChangedConvention>();

        /// <summary>
        ///     Conventions to run when the properties or the principal key of a foreign key are changed.
        /// </summary>
        public virtual IList<IForeignKeyPropertiesChangedConvention> ForeignKeyPropertiesChangedConventions { get; }
            = new List<IForeignKeyPropertiesChangedConvention>();

        /// <summary>
        ///     Conventions to run when the uniqueness of a foreign key is changed.
        /// </summary>
        public virtual IList<IForeignKeyUniquenessChangedConvention> ForeignKeyUniquenessChangedConventions { get; }
            = new List<IForeignKeyUniquenessChangedConvention>();

        /// <summary>
        ///     Conventions to run when the requiredness of a foreign key is changed.
        /// </summary>
        public virtual IList<IForeignKeyRequirednessChangedConvention> ForeignKeyRequirednessChangedConventions { get; }
            = new List<IForeignKeyRequirednessChangedConvention>();

        /// <summary>
        ///     Conventions to run when the ownership of a foreign key is changed.
        /// </summary>
        public virtual IList<IForeignKeyOwnershipChangedConvention> ForeignKeyOwnershipChangedConventions { get; }
            = new List<IForeignKeyOwnershipChangedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is changed on a foreign key.
        /// </summary>
        public virtual IList<IForeignKeyAnnotationChangedConvention> ForeignKeyAnnotationChangedConventions { get; } =
            new List<IForeignKeyAnnotationChangedConvention>();

        /// <summary>
        ///     Conventions to run when a navigation property is added.
        /// </summary>
        public virtual IList<INavigationAddedConvention> NavigationAddedConventions { get; } = new List<INavigationAddedConvention>();

        /// <summary>
        ///     Conventions to run when a navigation property is removed.
        /// </summary>
        public virtual IList<INavigationRemovedConvention> NavigationRemovedConventions { get; } = new List<INavigationRemovedConvention>();

        /// <summary>
        ///     Conventions to run when a key is added.
        /// </summary>
        public virtual IList<IKeyAddedConvention> KeyAddedConventions { get; } = new List<IKeyAddedConvention>();

        /// <summary>
        ///     Conventions to run when a key is removed.
        /// </summary>
        public virtual IList<IKeyRemovedConvention> KeyRemovedConventions { get; } = new List<IKeyRemovedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is changed on a key.
        /// </summary>
        public virtual IList<IKeyAnnotationChangedConvention> KeyAnnotationChangedConventions { get; } =
            new List<IKeyAnnotationChangedConvention>();

        /// <summary>
        ///     Conventions to run when an index is added.
        /// </summary>
        public virtual IList<IIndexAddedConvention> IndexAddedConventions { get; } = new List<IIndexAddedConvention>();

        /// <summary>
        ///     Conventions to run when an index is removed.
        /// </summary>
        public virtual IList<IIndexRemovedConvention> IndexRemovedConventions { get; } = new List<IIndexRemovedConvention>();

        /// <summary>
        ///     Conventions to run when the uniqueness of an index is changed.
        /// </summary>
        public virtual IList<IIndexUniquenessChangedConvention> IndexUniquenessChangedConventions { get; } =
            new List<IIndexUniquenessChangedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is changed on an index.
        /// </summary>
        public virtual IList<IIndexAnnotationChangedConvention> IndexAnnotationChangedConventions { get; } =
            new List<IIndexAnnotationChangedConvention>();

        /// <summary>
        ///     Conventions to run when a property is added.
        /// </summary>
        public virtual IList<IPropertyAddedConvention> PropertyAddedConventions { get; } = new List<IPropertyAddedConvention>();

        /// <summary>
        ///     Conventions to run when the nullability of a property is changed.
        /// </summary>
        public virtual IList<IPropertyNullabilityChangedConvention> PropertyNullabilityChangedConventions { get; }
            = new List<IPropertyNullabilityChangedConvention>();

        /// <summary>
        ///     Conventions to run when the field of a property is changed.
        /// </summary>
        public virtual IList<IPropertyFieldChangedConvention> PropertyFieldChangedConventions { get; }
            = new List<IPropertyFieldChangedConvention>();

        /// <summary>
        ///     Conventions to run when an annotation is changed on a property.
        /// </summary>
        public virtual IList<IPropertyAnnotationChangedConvention> PropertyAnnotationChangedConventions { get; }
            = new List<IPropertyAnnotationChangedConvention>();

        /// <summary>
        ///     <para>
        ///         Call this method to build a <see cref="ConventionSet" /> for only core services when using
        ///         the <see cref="ModelBuilder" /> outside of <see cref="DbContext.OnModelCreating" />.
        ///     </para>
        ///     <para>
        ///         Note that it is unusual to use this method.
        ///         Consider using <see cref="DbContext" /> in the normal way instead.
        ///     </para>
        /// </summary>
        /// <returns> The convention set. </returns>
        public static ConventionSet CreateConventionSet([NotNull] DbContext context)
        {
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Model.Validation>>();

            var conventionSet = context.GetService<IConventionSetBuilder>().CreateConventionSet();

            conventionSet.ModelFinalizedConventions.Add(
                new ValidatingConvention(context.GetService<IModelValidator>(), logger));

            return conventionSet;
        }
    }
}
