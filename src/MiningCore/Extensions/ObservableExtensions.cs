/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;

namespace MiningCore.Extensions
{
    public static class ObservableExtensions
    {
        public static IObservable<T> Spy<T>(this IObservable<T> source, string opName = "IObservable")
        {
            Console.WriteLine("{0}: Observable obtained on Thread: {1}", opName, Thread.CurrentThread.ManagedThreadId);

            return Observable.Create<T>(obs =>
            {
                Console.WriteLine("{0}: Subscribed to on Thread: {1}", opName, Thread.CurrentThread.ManagedThreadId);

                try
                {
                    var subscription = source
                        .Do(
                            x => Console.WriteLine("{0}: OnNext({1}) on Thread: {2}", opName, x,
                                Thread.CurrentThread.ManagedThreadId),
                            ex => Console.WriteLine("{0}: OnError({1}) on Thread: {2}", opName, ex,
                                Thread.CurrentThread.ManagedThreadId),
                            () => Console.WriteLine("{0}: OnCompleted() on Thread: {1}", opName,
                                Thread.CurrentThread.ManagedThreadId))
                        .Subscribe(obs);

                    return new CompositeDisposable(
                        subscription,
                        Disposable.Create(() => Console.WriteLine("{0}: Cleaned up on Thread: {1}", opName,
                            Thread.CurrentThread.ManagedThreadId)));
                }

                finally
                {
                    Console.WriteLine("{0}: Subscription completed.", opName);
                }
            });
        }
    }
}
