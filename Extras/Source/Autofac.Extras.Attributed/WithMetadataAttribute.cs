﻿// This software is part of the Autofac IoC container
// Copyright © 2013 Autofac Contributors
// http://autofac.org
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Autofac.Features.Metadata;

namespace Autofac.Extras.Attributed
{
    /// <summary>
    /// Provides an annotation to filter constructor dependencies 
    /// according to their specified metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This attribute allows constructor dependencies to be filtered by metadata.
    /// By marking your dependencies with this attribute and associating
    /// an attribute filter with your type registration, you can be selective
    /// about which service registration should be used to provide the
    /// dependency.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// A simple example might be registration of a specific logger type to be
    /// used by a class. If many loggers are registered with the <c>LoggerName</c>
    /// metadata, the consumer can simply specify the filter as an attribute to
    /// the constructor parameter.
    /// </para>
    /// <code lang="C#">
    /// public class Manager
    /// {
    ///   public Manager([WithMetadata("LoggerName", "Manager")] ILogger logger)
    ///   {
    ///     // ...
    ///   }
    /// }
    /// </code>
    /// <para>
    /// The same thing can be done for enumerable:
    /// </para>
    /// <code lang="C#">
    /// public class SolutionExplorer
    /// {
    ///   public SolutionExplorer(
    ///     [WithMetadata("Target", "Solution")] IEnumerable&lt;IAdapter&gt; adapters,
    ///     [WithMetadata("LoggerName", "Solution")] ILogger logger)
    ///   {
    ///     this.Adapters = adapters.ToList();
    ///     this.Logger = logger;
    ///   }
    /// }
    /// </code>
    /// <para>
    /// When registering your components, the associated metadata on the
    /// dependencies will be used. Be sure to specify the
    /// <see cref="Autofac.Extras.Attributed.AutofacAttributeExtensions.WithAttributeFilter{TLimit, TReflectionActivatorData, TStyle}" />
    /// extension on the type with the filtered constructor parameters.
    /// </para>
    /// <code lang="C#">
    /// var builder = new ContainerBuilder();
    /// builder.RegisterModule(new AttributedMetadataModule());
    /// 
    /// // Attach metadata to the components getting filtered
    /// builder.RegisterType&lt;ConsoleLogger&gt;().WithMetadata(&quot;LoggerName&quot;, &quot;Solution&quot;).As&lt;ILogger&gt;();
    /// builder.RegisterType&lt;FileLogger&gt;().WithMetadata(&quot;LoggerName&quot;, &quot;Other&quot;).As&lt;ILogger&gt;();
    /// 
    /// // Attach the filtering behavior to the component with the constructor
    /// builder.RegisterType&lt;SolutionExplorer&gt;().WithAttributeFilter();
    /// 
    /// var container = builder.Build();
    /// 
    /// // The resolved instance will have the appropriate services in place
    /// var explorer = container.Resolve&lt;SolutionExplorer&gt;();
    /// </code>
    /// </example>
    [SuppressMessage("Microsoft.Design", "CA1018:MarkAttributesWithAttributeUsage", Justification = "Allowing the inherited AttributeUsageAttribute to be used avoids accidental override or conflict at this level.")]
    public sealed class WithMetadataAttribute : ParameterFilterAttribute
    {
        /// <summary>
        /// Reference to the <see cref="Autofac.Extras.Attributed.WithMetadataAttribute.FilterOne{T}"/>
        /// method used in creating a closed generic reference during registration.
        /// </summary>
        private static readonly MethodInfo filterOne = typeof(WithMetadataAttribute).GetMethod("FilterOne", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod);

        /// <summary>
        /// Reference to the <see cref="Autofac.Extras.Attributed.WithMetadataAttribute.FilterAll{T}"/>
        /// method used in creating a closed generic reference during registration.
        /// </summary>
        private static readonly MethodInfo filterAll = typeof(WithMetadataAttribute).GetMethod("FilterAll", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.InvokeMethod);

        /// <summary>
        /// Initializes a new instance of the <see cref="WithMetadataAttribute"/> class, 
        /// specifying the <paramref name="key"/> and <paramref name="value"/> that the 
        /// dependency should have in order to satisfy the parameter.
        /// </summary>
        public WithMetadataAttribute(string key, object value)
        {
            this.Key = key;
            this.Value = value;
        }

        /// <summary>
        /// Gets the key the dependency is expected to have to satisfy the parameter.
        /// </summary>
        /// <value>
        /// The <see cref="System.String"/> corresponding to a registered metadata
        /// key on a component. Resolved components must have this metadata key to
        /// satisfy the filter.
        /// </value>
        public string Key { get; private set; }

        /// <summary>
        /// Gets the value the dependency is expected to have to satisfy the parameter.
        /// </summary>
        /// <value>
        /// The <see cref="System.Object"/> corresponding to a registered metadata
        /// value on a component. Resolved components must have the metadata
        /// <see cref="Autofac.Extras.Attributed.WithMetadataAttribute.Key"/> with
        /// this value to satisfy the filter.
        /// </value>
        public object Value { get; private set; }

        /// <summary>
        /// Resolves a constructor parameter based on metadata requirements.
        /// </summary>
        /// <param name="parameter">The specific parameter being resolved that is marked with this attribute.</param>
        /// <param name="context">The component context under which the parameter is being resolved.</param>
        /// <returns>
        /// The instance of the object that should be used for the parameter value.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown if <paramref name="parameter" /> or <paramref name="context" /> is <see langword="null" />.
        /// </exception>
        public override object ResolveParameter(ParameterInfo parameter, IComponentContext context)
        {
            if (parameter == null)
            {
                throw new ArgumentNullException("parameter");
            }
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            // GetElementType currently is the effective equivalent of "Determine if the type
            // is in IEnumerable and if it is, get the type being enumerated." This doesn't support
            // the other relationship types like Lazy<T>, Func<T>, etc. If we need to add that,
            // this is the place to do it.
            var elementType = GetElementType(parameter.ParameterType);
            var hasMany = elementType != parameter.ParameterType;

            if (hasMany)
            {
                return filterAll.MakeGenericMethod(elementType).Invoke(null, new object[] { context, this.Key, this.Value });
            }

            return filterOne.MakeGenericMethod(elementType).Invoke(null, new object[] { context, this.Key, this.Value });
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            return type;
        }

        private static T FilterOne<T>(IComponentContext context, string metadataKey, object metadataValue)
        {
            // Using Lazy<T> to ensure components that aren't actually used won't get activated.
            return context.Resolve<IEnumerable<Meta<Lazy<T>>>>()
                .Where(m => m.Metadata.ContainsKey(metadataKey) && metadataValue.Equals(m.Metadata[metadataKey]))
                .Select(m => m.Value.Value)
                .FirstOrDefault();
        }

        private static IEnumerable<T> FilterAll<T>(IComponentContext context, string metadataKey, object metadataValue)
        {
            // Using Lazy<T> to ensure components that aren't actually used won't get activated.
            return context.Resolve<IEnumerable<Meta<Lazy<T>>>>()
                .Where(m => m.Metadata.ContainsKey(metadataKey) && metadataValue.Equals(m.Metadata[metadataKey]))
                .Select(m => m.Value.Value)
                .ToArray();
        }
    }
}