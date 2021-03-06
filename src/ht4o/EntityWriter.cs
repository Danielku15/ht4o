﻿/** -*- C# -*-
 * Copyright (C) 2010-2016 Thalmann Software & Consulting, http://www.softdev.ch
 *
 * This file is part of ht4o.
 *
 * ht4o is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 3
 * of the License, or any later version.
 *
 * Hypertable is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA
 * 02110-1301, USA.
 */

namespace Hypertable.Persistence
{
    using System;
    using System.Globalization;
    using Hypertable.Persistence.Collections;
    using Hypertable.Persistence.Scanner;
    using Hypertable.Persistence.Serialization;
    using EntitySpecSet =
        Hypertable.Persistence.Collections.Concurrent.ConcurrentSet<Hypertable.Persistence.Scanner.EntitySpec>;

    /// <summary>
    ///     The entity writer.
    /// </summary>
    internal sealed class EntityWriter
    {
        #region Fields

        /// <summary>
        ///     The behaviors.
        /// </summary>
        private readonly Behaviors behaviors;

        /// <summary>
        ///     Keeps track of the entities written.
        /// </summary>
        private readonly IdentitySet entitiesWritten;

        /// <summary>
        ///     The entity.
        /// </summary>
        private readonly object entity;

        /// <summary>
        ///     The entity context.
        /// </summary>
        private readonly EntityContext entityContext;

        /// <summary>
        ///     The entity specs fetched.
        /// </summary>
        private readonly EntitySpecSet entitySpecsFetched;

        /// <summary>
        ///     The entity specs written.
        /// </summary>
        private readonly EntitySpecSet entitySpecsWritten;

        /// <summary>
        ///     The entity type.
        /// </summary>
        private readonly Type entityType;

        /// <summary>
        ///     The entity reference.
        /// </summary>
        private EntityReference entityReference;

        /// <summary>
        ///     The entity key.
        /// </summary>
        private Key key;

        /// <summary>
        ///     Indicating whether entity is a new entity or not.
        /// </summary>
        private bool newEntity;

        /// <summary>
        ///     The table mutator.
        /// </summary>
        private ITableMutator tableMutator;

        /// <summary>
        ///     The table mutator type.
        /// </summary>
        private Type tableMutatorType;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="EntityWriter" /> class.
        /// </summary>
        /// <param name="entityContext">
        ///     The entity context.
        /// </param>
        /// <param name="entityType">
        ///     The entity type.
        /// </param>
        /// <param name="entity">
        ///     The entity.
        /// </param>
        /// <param name="behaviors">
        ///     The behaviors.
        /// </param>
        /// <param name="entitiesWritten">
        ///     The entities written.
        /// </param>
        /// <exception cref="ArgumentException">
        ///     If <see cref="Behaviors.DoNotCache" /> has been combined with <see cref="Behaviors.CreateLazy" />.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     If <see cref="Behaviors.BypassWriteCache" /> has been combined with <see cref="Behaviors.CreateLazy" />.
        /// </exception>
        private EntityWriter(EntityContext entityContext, Type entityType, object entity, Behaviors behaviors,
            IdentitySet entitiesWritten)
        {
            if (behaviors.IsCreateLazy())
            {
                if (behaviors.DoNotCache())
                {
                    throw new ArgumentException(@"DontCache cannot be combined with CreateLazy", nameof(behaviors));
                }

                if (behaviors.BypassWriteCache())
                {
                    throw new ArgumentException(@"BypassWriteCache cannot be combined with CreateLazy", nameof(behaviors));
                }
            }

            this.entityContext = entityContext;
            this.behaviors = behaviors;
            this.entityType = entityType;
            this.entity = entity;
            this.entitiesWritten = entitiesWritten;
            this.entitySpecsWritten = entityContext.EntitySpecsWritten;
            this.entitySpecsFetched = entityContext.EntitySpecsFetched;
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <param name="entityContext">
        ///     The entity context.
        /// </param>
        /// <param name="entity">
        ///     The entity to persist.
        /// </param>
        /// <param name="behaviors">
        ///     The behaviors.
        /// </param>
        /// <typeparam name="T">
        ///     The entity type.
        /// </typeparam>
        internal static void Persist<T>(EntityContext entityContext, T entity, Behaviors behaviors) where T : class
        {
            Persist(entityContext, typeof(T), entity, behaviors);
        }

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <param name="entityContext">
        ///     The entity context.
        /// </param>
        /// <param name="entityType">
        ///     The entity Type.
        /// </param>
        /// <param name="entity">
        ///     The entity to persist.
        /// </param>
        /// <param name="behaviors">
        ///     The behaviors.
        /// </param>
        internal static void Persist(EntityContext entityContext, Type entityType, object entity, Behaviors behaviors)
        {
            Persist(entityContext, entityType, entity, behaviors, new IdentitySet());
        }

        /// <summary>
        ///     Persist the given entity using the behavior specified.
        /// </summary>
        /// <param name="entityContext">
        ///     The entity context.
        /// </param>
        /// <param name="entityType">
        ///     The entity type.
        /// </param>
        /// <param name="entity">
        ///     The entity.
        /// </param>
        /// <param name="behaviors">
        ///     The behaviors.
        /// </param>
        /// <param name="entitiesWritten">
        ///     The entities written.
        /// </param>
        /// <returns>
        ///     The entity key.
        /// </returns>
        private static Key Persist(EntityContext entityContext, Type entityType, object entity, Behaviors behaviors,
            IdentitySet entitiesWritten)
        {
            var entityWriter = new EntityWriter(entityContext, entityType, entity, behaviors, entitiesWritten);
            return entityWriter.Persist();
        }

        /// <summary>
        ///     Persist the entity.
        /// </summary>
        /// <returns>
        ///     The entity key.
        /// </returns>
        /// <exception cref="PersistenceException">
        ///     If entity is not recognized as a valid entity.
        /// </exception>
        private Key Persist()
        {
            if (this.entitiesWritten.Add(this.entity))
            {
                var value = EntitySerializer.Serialize(this.entityContext, this.entityType, this.entity,
                    SerializationBase.DefaultCapacity, this.SerializingEntity);
                if (this.entityReference == null)
                {
                    throw new PersistenceException(string.Format(CultureInfo.InvariantCulture,
                        @"{0} is not a valid entity", this.entityType));
                }

                var dontCache = this.behaviors.DoNotCache();
                var bypassEntitySpecsFetched =
                    this.newEntity || this.behaviors.IsCreateNew() || this.behaviors.BypassReadCache();
                var entitySpec = !dontCache || !bypassEntitySpecsFetched
                    ? new EntitySpec(this.entityReference, new Key(this.key))
                    : null;

                if (dontCache || this.entitySpecsWritten.Add(entitySpec) || this.behaviors.BypassWriteCache())
                {
                    if (bypassEntitySpecsFetched || !this.entitySpecsFetched.Contains(entitySpec))
                    {
                        var type = this.entityReference.EntityType;
                        if (this.tableMutatorType != type)
                        {
                            this.tableMutatorType = type;
                            this.tableMutator = this.entityContext.GetTableMutator(this.entityReference.Namespace,
                                this.entityReference.TableName);
                        }

                        //// TODO verbosity?
                        //// Logging.TraceEvent(TraceEventType.Verbose, () => string.Format(CultureInfo.InvariantCulture, @"Set {0}@{1}", this.tableMutator.Key, this.key));
                        this.tableMutator.Set(this.key, value);
                    }
                }
            }

            return this.key;
        }

        /// <summary>
        ///     The serializing entity callback.
        /// </summary>
        /// <param name="isRoot">
        ///     Indicating whether the entity is the root entity.
        /// </param>
        /// <param name="er">
        ///     The entity reference.
        /// </param>
        /// <param name="serializeType">
        ///     The serialize type.
        /// </param>
        /// <param name="e">
        ///     The entity.
        /// </param>
        /// <returns>
        ///     The entity key.
        /// </returns>
        private Key SerializingEntity(bool isRoot, EntityReference er, Type serializeType, object e)
        {
            if (isRoot)
            {
                this.entityReference = er;

                switch (this.behaviors & Behaviors.CreateBehaviors)
                {
                    case Behaviors.CreateAlways:
                        this.key = er.GenerateKey(e);
                        this.newEntity = true;
                        break;

                    case Behaviors.CreateLazy:
                        this.key = er.GetKeyFromEntity(e, out this.newEntity);
                        if (!this.newEntity)
                        {
                            var entitySpec = new EntitySpec(this.entityReference, new Key(this.key));

                            ////TODO needs to check if the entity has been modified (write,write again)
                            if (!this.entitySpecsWritten.Contains(entitySpec))
                            {
                                ////TODO needs to check if the entity has been modified (read, write)
                                if (this.behaviors.BypassReadCache() || !this.entitySpecsFetched.Contains(entitySpec))
                                {
                                    this.key = er.GenerateKey(e);
                                }
                            }
                            else
                            {
                                return null; // cancel further serialization
                            }
                        }

                        break;

                    case Behaviors.CreateNew:
                        this.key = er.GetKeyFromEntity(e, out this.newEntity);
                        break;
                }

                return this.key;
            }

            return Persist(this.entityContext, serializeType, e, this.behaviors, this.entitiesWritten);
        }

        #endregion
    }
}