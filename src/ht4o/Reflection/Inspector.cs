/** -*- C# -*-
 * Copyright (C) 2010-2013 Thalmann Software & Consulting, http://www.softdev.ch
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
namespace Hypertable.Persistence.Reflection
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;

    using Hypertable.Persistence.Collections;
    using Hypertable.Persistence.Extensions;

    /// <summary>
    /// The inspector.
    /// </summary>
    internal sealed class Inspector
    {
        #region Static Fields

        /// <summary>
        /// The inspectors.
        /// </summary>
        private static readonly ConcurrentTypeDictionary<Inspector> Inspectors = new ConcurrentTypeDictionary<Inspector>();

        #endregion

        #region Fields

        /// <summary>
        /// The enumerable.
        /// </summary>
        private readonly InspectedEnumerable enumerable;

        /// <summary>
        /// Indicating whether the inspected type has a default constructor.
        /// </summary>
        private readonly bool hasDefaultconstructor;

        /// <summary>
        /// Indicating whether the inspected type has a serialization handlers.
        /// </summary>
        private readonly bool hasSerializationHandlers;

        /// <summary>
        /// The inspected properties.
        /// </summary>
        private readonly IDictionary<string, InspectedProperty> inspectedProperties;

        /// <summary>
        /// The serializable.
        /// </summary>
        private readonly InspectedSerializable serializable;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Inspector"/> class.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        private Inspector(Type type)
        {
            this.InspectedType = type;

            if (!typeof(Enumerable).IsAssignableFrom(type))
            {
                this.hasDefaultconstructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[0], null) != null;

                ////TODO control isSerializable/ISerializable by settings fields or props?
                this.inspectedProperties = type.HasAttribute<SerializableAttribute>() ? InspectFields(type) : InspectProperties(type);

                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    ////TODO impl support for comparer (also for set's)
                }
                else if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    this.enumerable = InspectEnumerable(type);
                }
                else if (type.HasInterface(typeof(ISerializable)))
                {
                    this.serializable = InspectSerializable(type);
                    if (this.serializable != null)
                    {
                        this.inspectedProperties.Clear();
                    }
                }

                this.OnSerializing = CreateHandler<OnSerializingAttribute>(type);
                this.OnSerialized = CreateHandler<OnSerializedAttribute>(type);
                this.OnDeserializing = CreateHandler<OnDeserializingAttribute>(type);
                this.OnDeserialized = CreateHandler<OnDeserializedAttribute>(type);
                this.hasSerializationHandlers = this.OnSerializing != null || this.OnSerialized != null || this.OnDeserializing != null || this.OnDeserialized != null;
            }
            else
            {
                this.enumerable = InspectEnumerable(type);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the inspected enumerable.
        /// </summary>
        /// <value>
        /// The inspected enumerable.
        /// </value>
        internal InspectedEnumerable Enumerable
        {
            get
            {
                return this.enumerable;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the inspector has properties.
        /// </summary>
        /// <value>
        /// <c>true</c> if the inspector has properties, otherwise <c>false</c>.
        /// </value>
        internal bool HasProperties
        {
            get
            {
                return this.inspectedProperties.Count > 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the inspected type has a serialization handlers.
        /// </summary>
        /// <value>
        /// <c>true</c> if the inspected type has a serialization handlers, otherwise <c>false</c>.
        /// </value>
        internal bool HasSerializationHandlers
        {
            get
            {
                return this.hasSerializationHandlers;
            }
        }

        /// <summary>
        /// Gets the inspected type.
        /// </summary>
        /// <value>
        /// The inspected type.
        /// </value>
        internal Type InspectedType { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the inspector is a collection.
        /// </summary>
        /// <value>
        /// <c>true</c> if the inspector is a collection, otherwise <c>false</c>.
        /// </value>
        internal bool IsCollection
        {
            get
            {
                return this.enumerable != null && this.enumerable.HasAdd;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the inspector is an enumerable.
        /// </summary>
        /// <value>
        /// <c>true</c> if the inspector is an enumerable, otherwise <c>false</c>.
        /// </value>
        internal bool IsEnumerable
        {
            get
            {
                return this.enumerable != null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the inspector is a serializable.
        /// </summary>
        /// <value>
        /// <c>true</c> if the inspector is a serializable, otherwise <c>false</c>.
        /// </value>
        internal bool IsSerializable
        {
            get
            {
                return this.serializable != null;
            }
        }

        /// <summary>
        /// Gets the OnDeserialized handler.
        /// </summary>
        /// <value>
        /// The OnDeserialized handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnDeserialized { get; private set; }

        /// <summary>
        /// Gets the OnDeserializing handler.
        /// </summary>
        /// <value>
        /// The OnDeserializing handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnDeserializing { get; private set; }

        /// <summary>
        /// Gets the OnSerialized handler.
        /// </summary>
        /// <value>
        /// The OnSerialized handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnSerialized { get; private set; }

        /// <summary>
        /// Gets the OnSerializing handler.
        /// </summary>
        /// <value>
        /// The OnSerializing handler or null.
        /// </value>
        internal Action<object, StreamingContext> OnSerializing { get; private set; }

        /// <summary>
        /// Gets the inspected properties.
        /// </summary>
        /// <value>
        /// The inspected properties.
        /// </value>
        internal ICollection<InspectedProperty> Properties
        {
            get
            {
                return this.inspectedProperties.Values;
            }
        }

        /// <summary>
        /// Gets the inspected enumerable.
        /// </summary>
        /// <value>
        /// The inspected enumerable.
        /// </value>
        internal InspectedSerializable Serializable
        {
            get
            {
                return this.serializable;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the inspector for the type specified.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        /// <returns>
        /// The inspector.
        /// </returns>
        internal static Inspector InspectorForType(Type type)
        {
            return Inspectors.GetOrAdd(type, t => new Inspector(t));
        }

        /// <summary>
        /// Creates an instance of the inspected type.
        /// </summary>
        /// <returns>
        /// The newly created instance.
        /// </returns>
        internal object CreateInstance()
        {
            if (!this.InspectedType.IsInterface && !this.InspectedType.IsAbstract)
            {
                return this.hasDefaultconstructor ? Activator.CreateInstance(this.InspectedType, true) : FormatterServices.GetUninitializedObject(this.InspectedType);
            }

            return null;
        }

        /// <summary>
        /// Gets the inspected property by the name specified.
        /// </summary>
        /// <param name="name">
        /// The property name.
        /// </param>
        /// <returns>
        /// The inspected property or null.
        /// </returns>
        internal InspectedProperty GetProperty(string name)
        {
            InspectedProperty inspectedProperty;
            this.inspectedProperties.TryGetValue(name, out inspectedProperty);
            return inspectedProperty;
        }

        /// <summary>
        /// Create a handler for serialization attribute specified.
        /// </summary>
        /// <param name="type">
        /// The type.
        /// </param>
        /// <typeparam name="T">
        /// The serialization attribute.
        /// </typeparam>
        /// <returns>
        /// The newly created handler or null.
        /// </returns>
        private static Action<object, StreamingContext> CreateHandler<T>(Type type) where T : Attribute
        {
            var methods = type.GetMethodsWithAttribute<T>();
            if (methods != null)
            {
                var handler = new DynamicMethod("Handler" + methods[0].Name, typeof(void), new[] { typeof(object), typeof(StreamingContext) }, methods[0].Module, true);
                var generator = handler.GetILGenerator();

                foreach (var m in methods)
                {
                    generator.Emit(OpCodes.Ldarg_0);
                    generator.Emit(OpCodes.Ldarg_1);
                    generator.Emit(OpCodes.Call, m);
                }

                generator.Emit(OpCodes.Ret);

                return (Action<object, StreamingContext>)handler.CreateDelegate(typeof(Action<object, StreamingContext>));
            }

            return null;
        }

        /// <summary>
        /// Inspects enumerable for the type specified.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        /// <returns>
        /// The inspected enumerable.
        /// </returns>
        private static InspectedEnumerable InspectEnumerable(Type type)
        {
            return new InspectedEnumerable(type);
        }

        /// <summary>
        /// Inspects the fields for the type specified.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        /// <returns>
        /// The inspected fields.
        /// </returns>
        private static IDictionary<string, InspectedProperty> InspectFields(Type type)
        {
            var inspectedProperties = new Dictionary<string, InspectedProperty>(StringComparer.OrdinalIgnoreCase);
            for (var t = type; t != typeof(object) && t != null; t = t.BaseType)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

                //// TODO correct flattening (duplicated private field names)
                //// TODO add any filter if required
                foreach (var field in fields)
                {
                    if (!field.FieldType.IsTransient())
                    {
                        var name = field.SerializableName();
                        if (!inspectedProperties.ContainsKey(name))
                        {
                            var inspectedProperty = new InspectedProperty(type, field);
                            if (inspectedProperty.HasSetter && inspectedProperty.HasGetter && !inspectedProperty.IsTransient)
                            {
                                inspectedProperties.Add(name, inspectedProperty);
                            }
                        }
                        else
                        {
                            ////TODO ? throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, @"Ambiguous field {0} in {1}", name, type), "type");
                        }
                    }
                }
            }

            return inspectedProperties;
        }

        /// <summary>
        /// Inspects the properties for the type specified.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        /// <returns>
        /// The inspected properties.
        /// </returns>
        private static IDictionary<string, InspectedProperty> InspectProperties(Type type)
        {
            var inspectedProperties = new Dictionary<string, InspectedProperty>(StringComparer.OrdinalIgnoreCase);
            for (var t = type; t != typeof(object) && t != null; t = t.BaseType)
            {
                var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

                //// TODO correct flattening (duplicated private property names)
                //// TODO add any filter if required
                foreach (var property in properties)
                {
                    if (!property.PropertyType.IsTransient())
                    {
                        var name = property.SerializableName();
                        if (!inspectedProperties.ContainsKey(name))
                        {
                            // Ignore indexer
                            if (property.GetIndexParameters().Length == 0)
                            {
                                var inspectedProperty = new InspectedProperty(type, property);
                                if (inspectedProperty.HasSetter && inspectedProperty.HasGetter && !inspectedProperty.IsTransient)
                                {
                                    inspectedProperties.Add(name, inspectedProperty);
                                }
                            }
                        }
                        else
                        {
                            ////TODO ? throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, @"Ambiguous property {0} in {1}", name, type), "type");
                        }
                    }
                }
            }

            return inspectedProperties;
        }

        /// <summary>
        /// Inspects serializable for the type specified.
        /// </summary>
        /// <param name="type">
        /// The type to inspect.
        /// </param>
        /// <returns>
        /// The inspected serializable.
        /// </returns>
        private static InspectedSerializable InspectSerializable(Type type)
        {
            var inspectedSerializable = new InspectedSerializable(type);
            return inspectedSerializable.CreateInstance != null ? inspectedSerializable : null;
        }

        #endregion
    }
}