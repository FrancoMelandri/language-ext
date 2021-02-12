using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using LanguageExt.Common;
using System.Threading.Tasks;
using static LanguageExt.Prelude;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Threading;
using LanguageExt.Interfaces;
using LanguageExt.Thunks;

namespace LanguageExt
{
    /// <summary>
    /// Asynchronous effect monad
    /// </summary>
    public struct Aff<Env, A> 
        where Env : struct, HasCancel<Env>
    {
        internal ThunkAsync<Env, A> thunk;

        /// <summary>
        /// Constructor
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        internal Aff(ThunkAsync<Env, A> thunk) =>
            this.thunk = thunk ?? throw new ArgumentNullException(nameof(thunk));

        /// <summary>
        /// Invoke the effect
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public ValueTask<Fin<A>> RunIO(in Env env) =>
            thunk.Value(env).Map(ThunkR<A>.DisposeAcquired);

        /// <summary>
        /// Invoke the effect
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        public async ValueTask RunUnitIO(Env env) =>
            ignore(await thunk.Value(env).Map(ThunkR<A>.DisposeAcquired).ConfigureAwait(false));

        /// <summary>
        /// Invoke the effect
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        internal ValueTask<ThunkR<A>> InternalRunIO(in Env env) =>
            thunk.Value(env);

        /// <summary>
        /// Launch the async process without awaiting the result
        /// </summary>
        /// <returns></returns>
        [MethodImpl(AffOpt.mops)]
        public Aff<Env, Unit> FireAndForget()
        {
            var t = thunk;
            return Aff<Env, Unit>(env => { 
                ignore(t.Value(env).Map(ThunkR<A>.DisposeAcquired));
                return unit.AsValueTask();
            });
        }

        /// <summary>
        /// Lift an asynchronous effect into the Aff monad
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Effect(Func<Env, ValueTask<A>> f) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(f));

        /// <summary>
        /// Lift an asynchronous effect into the Aff monad
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> EffectMaybe(Func<Env, ValueTask<Fin<A>>> f) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(f));

        /// <summary>
        /// Lift an asynchronous effect into the Aff monad
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Effect(Func<Env, ValueTask> f) =>
            new Aff<Env, Unit>(ThunkAsync<Env, Unit>.Lazy(async e =>
            {
                await f(e).ConfigureAwait(false);
                return Fin<Unit>.Succ(default);
            }));

        /// <summary>
        /// Lift a value into the Aff monad 
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Success(A value) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Success(value));

        /// <summary>
        /// Lift a failure into the Aff monad 
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Fail(Error error) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Fail(error));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> operator |(Aff<Env, A> ma, Aff<Env, A> mb) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(
                async env =>
                {
                    var ra = await ma.InternalRunIO(env).ConfigureAwait(false);
                    return ra.IsSucc
                        ? ra
                        : await mb.InternalRunIO(env).ConfigureAwait(false) + ra;
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> operator |(Aff<Env, A> ma, LanguageExt.Aff<A> mb) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(
                async env =>
                {
                    var ra = await ma.RunIO(env).ConfigureAwait(false);
                    return ra.IsSucc
                        ? ra
                        : await mb.RunIO().ConfigureAwait(false);
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> operator |(LanguageExt.Aff<A> ma, Aff<Env, A> mb) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(
                async env =>
                {
                    var ra = await ma.RunIO().ConfigureAwait(false);
                    return ra.IsSucc
                        ? ra
                        : await mb.RunIO(env).ConfigureAwait(false);
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> operator |(Aff<Env, A> ma, Eff<Env, A> mb) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(
                async env =>
                {
                    var ra = await ma.RunIO(env).ConfigureAwait(false);
                    return ra.IsSucc
                        ? ra
                        : mb.RunIO(env);
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> operator |(Eff<Env, A> ma, Aff<Env, A> mb) =>
            new Aff<Env, A>(ThunkAsync<Env, A>.Lazy(
                async env =>
                {
                    var ra = ma.RunIO(env);
                    return ra.IsSucc
                        ? ra
                        : await mb.RunIO(env).ConfigureAwait(false);
                }));
        
        /// <summary>
        /// Clear the memoised value
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        public Unit Clear()
        {
            thunk = thunk.Clone();
            return default;
        }
        
        /// <summary>
        /// Implicit conversion from pure Aff
        /// </summary>
        public static implicit operator Aff<Env, A>(Aff<A> ma) =>
            EffectMaybe(env => ma.RunIO());

        /// <summary>
        /// Implicit conversion from pure Eff
        /// </summary>
        public static implicit operator Aff<Env, A>(Eff<A> ma) =>
            EffectMaybe(env => ma.RunIO().AsValueTask());

        /// <summary>
        /// Implicit conversion from Eff
        /// </summary>
        public static implicit operator Aff<Env, A>(Eff<Env, A> ma) =>
            EffectMaybe(env => ma.RunIO(env).AsValueTask());
    }

    public static partial class AffExtensions
    {
        //
        // Sequence (with tuples)
        //
        
        /// <summary>
        /// Run the two effects in the tuple in parallel, wait for them all to finish, then return a tuple of the results
        /// </summary>
        public static Aff<Env, (A, B)> Sequence<Env, A, B>(this (Aff<Env, A>, Aff<Env, B>) ms) where Env : struct, HasCancel<Env> => 
            AffMaybe<Env,(A, B)>(async env =>
            {
                var t1 = ms.Item1.RunIO(env).AsTask();
                var t2 = ms.Item2.RunIO(env).AsTask();
                
                var tasks = new Task[] {t1, t2};
                await Task.WhenAll(tasks);
                return from r1 in t1.Result
                       from r2 in t2.Result
                       select (r1, r2);
            });

        /// <summary>
        /// Run the three effects in the tuple in parallel, wait for them all to finish, then return a tuple of the results
        /// </summary>
        public static Aff<Env, (A, B, C)> Sequence<Env, A, B, C>(this (Aff<Env, A>, Aff<Env, B>, Aff<Env, C>) ms) where Env : struct, HasCancel<Env> => 
            AffMaybe<Env,(A, B, C)>(async env =>
            {
                var t1 = ms.Item1.RunIO(env).AsTask();
                var t2 = ms.Item2.RunIO(env).AsTask();
                var t3 = ms.Item3.RunIO(env).AsTask();
                
                var tasks = new Task[] {t1, t2, t3};
                await Task.WhenAll(tasks);
                return from r1 in t1.Result
                       from r2 in t2.Result
                       from r3 in t3.Result
                       select (r1, r2, r3);
            });

        /// <summary>
        /// Run the four effects in the tuple in parallel, wait for them all to finish, then return a tuple of the results
        /// </summary>
        public static Aff<Env, (A, B, C, D)> Sequence<Env, A, B, C, D>(this (Aff<Env, A>, Aff<Env, B>, Aff<Env, C>, Aff<Env, D>) ms) where Env : struct, HasCancel<Env> => 
            AffMaybe<Env,(A, B, C, D)>(async env =>
            {
                var t1 = ms.Item1.RunIO(env).AsTask();
                var t2 = ms.Item2.RunIO(env).AsTask();
                var t3 = ms.Item3.RunIO(env).AsTask();
                var t4 = ms.Item4.RunIO(env).AsTask();
                
                var tasks = new Task[] {t1, t2, t3, t4};
                await Task.WhenAll(tasks);
                return from r1 in t1.Result
                       from r2 in t2.Result
                       from r3 in t3.Result
                       from r4 in t4.Result
                       select (r1, r2, r3, r4);
            });

        /// <summary>
        /// Run the five effects in the tuple in parallel, wait for them all to finish, then return a tuple of the results
        /// </summary>
        public static Aff<Env, (A, B, C, D, E)> Sequence<Env, A, B, C, D, E>(this (Aff<Env, A>, Aff<Env, B>, Aff<Env, C>, Aff<Env, D>, Aff<Env, E>) ms) where Env : struct, HasCancel<Env> => 
            AffMaybe<Env,(A, B, C, D, E)>(async env =>
            {
                var t1 = ms.Item1.RunIO(env).AsTask();
                var t2 = ms.Item2.RunIO(env).AsTask();
                var t3 = ms.Item3.RunIO(env).AsTask();
                var t4 = ms.Item4.RunIO(env).AsTask();
                var t5 = ms.Item5.RunIO(env).AsTask();
                
                var tasks = new Task[] {t1, t2, t3, t4, t5};
                await Task.WhenAll(tasks);
                return from r1 in t1.Result
                       from r2 in t2.Result
                       from r3 in t3.Result
                       from r4 in t4.Result
                       from r5 in t5.Result
                       select (r1, r2, r3, r4, r5);
            });

        //
        // Map
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Map<Env, A, B>(this Aff<Env, A> ma, Func<A, B> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(f));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> MapAsync<Env, A, B>(this Aff<Env, A> ma, Func<A, ValueTask<B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.MapAsync(f));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> MapFail<Env, A>(this Aff<Env, A> ma, Func<Error, Error> f) where Env : struct, HasCancel<Env> =>
            ma.BiMap(identity, f);

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> MapFailAsync<Env, A>(this Aff<Env, A> ma, Func<Error, ValueTask<Error>> f) where Env : struct, HasCancel<Env> =>
            ma.BiMapAsync(x => x.AsValueTask(), f);

        //
        // Bi-map
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiMap<Env, A, B>(this Aff<Env, A> ma, Func<A, B> Succ, Func<Error, Error> Fail) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.BiMap(Succ, Fail));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiMapAsync<Env, A, B>(this Aff<Env, A> ma, Func<A, ValueTask<B>> Succ, Func<Error, ValueTask<Error>> Fail) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.BiMapAsync(Succ, Fail));

        //
        // Match
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, Func<A, B> Succ, Func<Error, B> Fail) where Env : struct, HasCancel<Env> =>
            Aff<Env, B>(async env =>
            {
                var r = await ma.RunIO(env).ConfigureAwait(false);
                return r.IsSucc
                    ? Succ(r.Value)
                    : Fail(r.Error);
            });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, Func<A, B> Succ, Aff<Env, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = await ma.RunIO(env).ConfigureAwait(false);
                return r.IsSucc
                    ? Succ(r.Value)
                    : await Fail.RunIO(env).ConfigureAwait(false);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, Aff<Env, B> Succ, Func<Error, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = await ma.RunIO(env).ConfigureAwait(false);
                return r.IsSucc
                    ? await Succ.RunIO(env).ConfigureAwait(false)
                    : Fail(r.Error);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, Aff<Env, B> Succ, Aff<Env, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = await ma.RunIO(env).ConfigureAwait(false);
                return r.IsSucc
                    ? await Succ.RunIO(env).ConfigureAwait(false)
                    : await Fail.RunIO(env).ConfigureAwait(false);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, B Succ, Func<Error, B> Fail) where Env : struct, HasCancel<Env> =>
            Aff<Env, B>(async env =>
            {
                var r = await ma.RunIO(env).ConfigureAwait(false);
                return r.IsSucc
                    ? Succ
                    : Fail(r.Error);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, Func<A, B> Succ, B Fail) where Env : struct, HasCancel<Env> =>
            Aff<Env, B>(async env =>
                        {
                            var r = await ma.RunIO(env).ConfigureAwait(false);
                            return r.IsSucc
                                       ? Succ(r.Value)
                                       : Fail;
                        });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Aff<Env, A> ma, B Succ, B Fail) where Env : struct, HasCancel<Env> =>
            Aff<Env, B>(async env =>
                        {
                            var r = await ma.RunIO(env).ConfigureAwait(false);
                            return r.IsSucc
                                       ? Succ
                                       : Fail;
                        });
        
        //
        // IfNone
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, Func<Error, A> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : Fin<A>.Succ(f(res.Error));
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, A alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : Fin<A>.Succ(alternative);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, Aff<Env, A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : await alternative.RunIO(env).ConfigureAwait(false);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, Aff<A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : await alternative.RunIO().ConfigureAwait(false);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, Eff<Env, A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : alternative.RunIO(env);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Aff<Env, A> ma, Eff<A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    return res.IsSucc
                               ? res
                               : alternative.RunIO();
                });
        
        //
        // Iter / Do
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Aff<Env, A> ma, Func<A, Unit> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        f(res.Value);
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Aff<Env, A> ma, Func<A, Aff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        ignore(await f(res.Value).RunIO(env).ConfigureAwait(false));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Aff<Env, A> ma, Func<A, Aff<Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        ignore(await f(res.Value).RunIO().ConfigureAwait(false));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Aff<Env, A> ma, Func<A, Eff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        ignore(f(res.Value).RunIO(env));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Aff<Env, A> ma, Func<A, Eff<Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        ignore(f(res.Value).RunIO());
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Aff<Env, A> ma, Func<A, Unit> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        f(res.Value);
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Aff<Env, A> ma, Func<A, Aff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        var ures = await f(res.Value).RunIO(env).ConfigureAwait(false);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Aff<Env, A> ma, Func<A, Eff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        var ures = f(res.Value).RunIO(env);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Aff<Env, A> ma, Func<A, Aff<Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        var ures = await f(res.Value).RunIO().ConfigureAwait(false);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Aff<Env, A> ma, Func<A, Eff<Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = await ma.RunIO(env).ConfigureAwait(false);
                    if (res.IsSucc)
                    {
                        var ures = f(res.Value).RunIO();
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        //
        // Filter / Where
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Filter<Env, A>(this Aff<Env, A> ma, Func<A, bool> f) where Env : struct, HasCancel<Env> =>
            ma.Bind(x => f(x) ? SuccessEff<A>(x) : FailEff<A>(Error.New(Thunk.CancelledText)));


        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Where<Env, A>(this Aff<Env, A> ma, Func<A, bool> f) where Env : struct, HasCancel<Env> =>
            Filter(ma, f);

        //
        // Bind
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Aff<Env, A> ma, Func<A, Eff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Aff<Env, A> ma, Func<A, LanguageExt.Eff<B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        //
        // Bi-Bind
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiBind<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<Env, B>> Succ, Func<Error, Aff<Env, B>> Fail) where Env : struct, HasCancel<Env> =>
            ma.Match(Succ, Fail)
                .Flatten();

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiBind<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<B>> Succ, Func<Error, Aff<B>> Fail) where Env : struct, HasCancel<Env> =>
            ma.Match(Succ, Fail)
                .Flatten();

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiBind<Env, A, B>(this Aff<Env, A> ma, Func<A, Eff<Env, B>> Succ, Func<Error, Eff<Env, B>> Fail) where Env : struct, HasCancel<Env> =>
            ma.Match(Succ, Fail)
                .Flatten();

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> BiBind<Env, A, B>(this Aff<Env, A> ma, Func<A, LanguageExt.Eff<B>> Succ, Func<Error, LanguageExt.Eff<B>> Fail) where Env : struct, HasCancel<Env> =>
            ma.Match(Succ, Fail)
                .Flatten();

        //
        // Flatten (monadic join)
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Aff<Env, Aff<Env, A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Aff<Env, Aff<A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Aff<Env, Eff<Env, A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Aff<Env, LanguageExt.Eff<A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());

        //
        // Select
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Select<Env, A, B>(this Aff<Env, A> ma, Func<A, B> f) where Env : struct, HasCancel<Env> =>
            Map(ma, f);

        //
        // SelectMany
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Aff<Env, A> ma, Func<A, Aff<B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Aff<Env, A> ma, Func<A, Eff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Aff<Env, A> ma, Func<A, LanguageExt.Eff<B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Aff<Env, A> ma, Func<A, Aff<Env, B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Aff<Env, A> ma, Func<A, Aff<B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Aff<Env, A> ma, Func<A, Eff<Env, B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Aff<Env, A> ma, Func<A, LanguageExt.Eff<B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));

        //
        // Zip
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Aff<Env, A> ma, Aff<Env, B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ta = ma.RunIO(e).AsTask();
                var tb = mb.RunIO(e).AsTask();
                await System.Threading.Tasks.Task.WhenAll(ta, tb).ConfigureAwait(false);
                if (ta.CompletedSuccessfully() && tb.CompletedSuccessfully())
                {
                    return ta.Result.IsSucc && tb.Result.IsSucc
                        ? Fin<(A, B)>.Succ((ta.Result.Value, tb.Result.Value))
                        : ta.Result.IsFail
                            ? Fin<(A, B)>.Fail(ta.Result.Error)
                            : Fin<(A, B)>.Fail(tb.Result.Error);
                }
                else
                {
                    return (ta.IsCanceled || tb.IsCanceled)
                        ? Fin<(A, B)>.Fail(Error.New(Thunk.CancelledText))
                        : ta.IsFaulted
                            ? Fin<(A, B)>.Fail(Error.New(ta.Exception))
                            : Fin<(A, B)>.Fail(Error.New(tb.Exception));
                }
            }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Aff<Env, A> ma, Aff<B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ta = ma.RunIO(e).AsTask();
                var tb = mb.RunIO().AsTask();
                await System.Threading.Tasks.Task.WhenAll(ta, tb).ConfigureAwait(false);
                if (ta.CompletedSuccessfully() && tb.CompletedSuccessfully())
                {
                    return ta.Result.IsSucc && tb.Result.IsSucc
                        ? Fin<(A, B)>.Succ((ta.Result.Value, tb.Result.Value))
                        : ta.Result.IsFail
                            ? Fin<(A, B)>.Fail(ta.Result.Error)
                            : Fin<(A, B)>.Fail(tb.Result.Error);
                }
                else
                {
                    return (ta.IsCanceled || tb.IsCanceled)
                        ? Fin<(A, B)>.Fail(Error.New(Thunk.CancelledText))
                        : ta.IsFaulted
                            ? Fin<(A, B)>.Fail(Error.New(ta.Exception))
                            : Fin<(A, B)>.Fail(Error.New(tb.Exception));
                }
            }));


        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Aff<A> ma, Aff<Env, B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ta = ma.RunIO().AsTask();
                var tb = mb.RunIO(e).AsTask();
                await System.Threading.Tasks.Task.WhenAll(ta, tb).ConfigureAwait(false);
                if (ta.CompletedSuccessfully() && tb.CompletedSuccessfully())
                {
                    return ta.Result.IsSucc && tb.Result.IsSucc
                        ? Fin<(A, B)>.Succ((ta.Result.Value, tb.Result.Value))
                        : ta.Result.IsFail
                            ? Fin<(A, B)>.Fail(ta.Result.Error)
                            : Fin<(A, B)>.Fail(tb.Result.Error);
                }
                else
                {
                    return (ta.IsCanceled || tb.IsCanceled)
                        ? Fin<(A, B)>.Fail(Error.New(Thunk.CancelledText))
                        : ta.IsFaulted
                            ? Fin<(A, B)>.Fail(Error.New(ta.Exception))
                            : Fin<(A, B)>.Fail(Error.New(tb.Exception));
                }
            }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Aff<Env, A> ma, LanguageExt.Eff<B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ta = ma.RunIO(e).AsTask();
                var ra = await ta.ConfigureAwait(false);
                if (!ta.CompletedSuccessfully())
                {
                    return Fin<(A, B)>.Fail(ra.Error);
                }

                var rb = mb.RunIO();
                if (rb.IsFail) return Fin<(A, B)>.Fail(rb.Error);

                return Fin<(A, B)>.Succ((ra.Value, rb.Value));
            }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this LanguageExt.Eff<A> ma, Aff<Env, B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ra = ma.RunIO();
                if (ra.IsFail) return Fin<(A, B)>.Fail(ra.Error);

                var tb = mb.RunIO(e).AsTask();
                var rb = await tb.ConfigureAwait(false);
                if (!tb.CompletedSuccessfully())
                {
                    return Fin<(A, B)>.Fail(rb.Error);
                }

                return Fin<(A, B)>.Succ((ra.Value, rb.Value));
            }));


        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Aff<Env, A> ma, Eff<Env, B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ta = ma.RunIO(e).AsTask();
                var ra = await ta.ConfigureAwait(false);
                if (!ta.CompletedSuccessfully())
                {
                    return Fin<(A, B)>.Fail(ra.Error);
                }

                var rb = mb.RunIO(e);
                if (rb.IsFail) return Fin<(A, B)>.Fail(rb.Error);

                return Fin<(A, B)>.Succ((ra.Value, rb.Value));
            }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, (A, B)> Zip<Env, A, B>(this Eff<Env, A> ma, Aff<Env, B> mb) where Env : struct, HasCancel<Env> =>
            new Aff<Env, (A, B)>(ThunkAsync<Env, (A, B)>.Lazy(async e =>
            {
                var ra = ma.RunIO(e);
                if (ra.IsFail) return Fin<(A, B)>.Fail(ra.Error);

                var tb = mb.RunIO(e).AsTask();
                var rb = await tb.ConfigureAwait(false);
                if (!tb.CompletedSuccessfully())
                {
                    return Fin<(A, B)>.Fail(rb.Error);
                }

                return Fin<(A, B)>.Succ((ra.Value, rb.Value));
            }));

        [Pure, MethodImpl(AffOpt.mops)]
        static ThunkAsync<Env, A> ThunkFromIO<Env, A>(Aff<Env, A> ma) where Env : struct, HasCancel<Env> =>
            ma.thunk;
    }
}
