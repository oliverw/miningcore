using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Miningcore.Contracts;

public class Contract
{
    [ContractAnnotation("predicate:false => halt")]
    public static void Requires<TException>(bool predicate, [CallerArgumentExpression("predicate")] string message = "")
        where TException : Exception, new()
    {
        if(!predicate)
        {
            var constructor = constructors.GetOrAdd(typeof(TException), CreateConstructor);
            throw constructor(new object[] { message });
        }
    }

    [ContractAnnotation("parameter:null => halt")]
    public static void RequiresNonNull(object parameter, [CallerArgumentExpression("parameter")] string message = "")
    {
        if(parameter == null)
            throw new ArgumentNullException($"{message} must not be null");
    }

    #region Exception Constructors

    private static readonly ConcurrentDictionary<Type, ConstructorDelegate> constructors = new();

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
