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
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;

namespace MiningCore.Contracts
{
    public class Contract
    {
        [ContractAnnotation("predicate:false => halt")]
        public static void Requires<TException>(bool predicate, string message = null)
            where TException : Exception, new()
        {
            if (!predicate)
            {
                var constructor = constructors.GetOrAdd(typeof(TException), CreateConstructor);
                throw constructor(new object[] { message });
            }
        }

        [ContractAnnotation("parameter:null => halt")]
        public static void RequiresNonNull(object parameter, string paramName)
        {
            if (parameter == null)
                throw new ArgumentNullException(paramName);
        }

        #region Exception Constructors

        private static readonly ConcurrentDictionary<Type, ConstructorDelegate> constructors = new ConcurrentDictionary<Type, ConstructorDelegate>();

        private delegate Exception ConstructorDelegate(object[] parameters);

        private static ConstructorDelegate CreateConstructor(Type type)
        {
            // Get the constructor info for these parameters
            var parameters = new[] { typeof(string) };
            var constructorInfo = type.GetTypeInfo().DeclaredConstructors.First(
                x => x.GetParameters().Length == 1 && x.GetParameters().First().ParameterType == typeof(string));
            var paramExpr = Expression.Parameter(typeof(object[]));

            // To feed the constructor with the right parameters, we need to generate an array 
            // of parameters that will be read from the initialize object array argument.
            var constructorParameters = parameters.Select((paramType, index) =>
                // convert the object[index] to the right constructor parameter type.
                    Expression.Convert(
                        // read a value from the object[index]
                        Expression.ArrayAccess(
                            paramExpr,
                            Expression.Constant(index)),
                        paramType)).ToArray();

            // just call the constructor.
            var body = Expression.New(constructorInfo, constructorParameters);

            var constructor = Expression.Lambda<ConstructorDelegate>(body, paramExpr);
            return constructor.Compile();
        }

        #endregion // Exception Constructors
    }
}
