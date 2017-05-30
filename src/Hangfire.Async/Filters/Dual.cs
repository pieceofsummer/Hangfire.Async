namespace Hangfire.Async.Filters
{
    /// <summary>
    /// Represents either sync or async job filter at given position in <see cref="DualCursor"/>.
    /// </summary>
    /// <typeparam name="TSync">Sync interface type</typeparam>
    /// <typeparam name="TAsync">Async interface type</typeparam>
    internal struct Dual<TSync, TAsync>
        where TSync : class
        where TAsync : class
    {
        public readonly TSync Sync;
        public readonly TAsync Async;
        
        public bool NotFound => Async == null && Sync == null;

        /// <summary>
        /// Initializes a <see cref="Dual{TSync, TAsync}"/> structure.
        /// </summary>
        /// <param name="sync">Synchronous implementation, or <c>null</c> if filter implements only asynchronous variant.</param>
        /// <param name="async">Asynchronous implementation, or <c>null</c> if filter implements only synchronous variant.</param>
        public Dual(TSync sync, TAsync async)
        {
            Sync = sync;
            Async = async;
        }
    }
}
