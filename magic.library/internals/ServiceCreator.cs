/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using System;
using magic.node.contracts;

namespace magic.library.internals
{
    internal class ServiceCreator<T> : IServiceCreator<T> where T : class
    {
        readonly IServiceProvider _provider;
        public ServiceCreator(IServiceProvider provider)
        {
            _provider = provider;
        }

        public T Create()
        {
            return _provider.GetService(typeof(T)) as T;
        }
    }
}