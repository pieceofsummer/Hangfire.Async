using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hangfire.Async.Filters
{
    /// <summary>
    /// Implements a lightweight bi-directional iterator over collection of job filters, 
    /// which can be either synchronous or asynchronous (or both).
    /// </summary>
    internal struct DualCursor
    {
        private int _index;
        private readonly object[] _filters;
        
        public DualCursor(object[] filters)
        {
            _index = 0;
            _filters = filters;
        }

        public void Reset()
        {
            _index = 0;
        }

        public void ResetToEnd()
        {
            _index = _filters.Length + 1;
        }

        /// <summary>
        /// Returns a next job filter matching at least one of the 
        /// <typeparamref name="TSync"/>, <typeparamref name="TAsync"/> types.
        /// </summary>
        /// <typeparam name="TSync">Sync filter type</typeparam>
        /// <typeparam name="TAsync">Async filter type</typeparam>
        public Dual<TSync, TAsync> GetNextFilter<TSync, TAsync>()
            where TSync : class
            where TAsync : class
        {
            while (_index < _filters.Length)
            {
                var filter = _filters[_index++];

                var sync = filter as TSync;
                var async = filter as TAsync;
                
                if (sync != null || async != null)
                {
                    return new Dual<TSync, TAsync>(sync, async);
                }
            }

            return default(Dual<TSync, TAsync>);
        }

        /// <summary>
        /// Returns a previous job filter matching at least one of the 
        /// <typeparamref name="TSync"/>, <typeparamref name="TAsync"/> types.
        /// </summary>
        /// <typeparam name="TSync">Sync filter type</typeparam>
        /// <typeparam name="TAsync">Async filter type</typeparam>
        public Dual<TSync, TAsync> GetPrevFilter<TSync, TAsync>()
            where TSync : class
            where TAsync : class
        {
            while (--_index > 0)
            {
                var filter = _filters[_index - 1];

                var sync = filter as TSync;
                var async = filter as TAsync;

                if (sync != null || async != null)
                {
                    return new Dual<TSync, TAsync>(sync, async);
                }
            }

            return default(Dual<TSync, TAsync>);
        }
    }
}
