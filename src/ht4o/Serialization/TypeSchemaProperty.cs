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
namespace Hypertable.Persistence.Serialization
{
    using Hypertable.Persistence.Reflection;

    /// <summary>
    /// The type schema property.
    /// </summary>
    internal struct TypeSchemaProperty
    {
        #region Fields

        /// <summary>
        /// The encoder info.
        /// </summary>
        public EncoderInfo EncoderInfo;

        /// <summary>
        /// The inspected property.
        /// </summary>
        public InspectedProperty InspectedProperty;

        /// <summary>
        /// The property name.
        /// </summary>
        public string PropertyName;

        #endregion
    }
}