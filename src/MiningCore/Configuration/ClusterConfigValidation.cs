using FluentValidation;
using System.Linq;

namespace MiningCore.Configuration
{
    /// <summary>
    /// Tagging interface
    /// </summary>
    public interface IValidateable
    {
    }

    public static class ValidateableExtensions
    {
        public static void Validate<T>(this T validationTarget, InlineValidator<T> validator)
            where T: IValidateable
        {
            var result = validator.Validate(validationTarget);

            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }
    }

    public partial class ClusterLoggingConfig : IValidateable
    {
        static readonly InlineValidator<ClusterLoggingConfig> validator;

        static ClusterLoggingConfig()
        {
            validator = new InlineValidator<ClusterLoggingConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class VarDiffConfig : IValidateable
    {
        static readonly InlineValidator<VarDiffConfig> validator;

        static VarDiffConfig()
        {
            validator = new InlineValidator<VarDiffConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class PoolBanningConfig : IValidateable
    {
        static readonly InlineValidator<PoolBanningConfig> validator;

        static PoolBanningConfig()
        {
            validator = new InlineValidator<PoolBanningConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class PoolPaymentProcessingConfig : IValidateable
    {
        static readonly InlineValidator<PoolPaymentProcessingConfig> validator;

        static PoolPaymentProcessingConfig()
        {
            validator = new InlineValidator<PoolPaymentProcessingConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class ClusterPaymentProcessingConfig : IValidateable
    {
        static readonly InlineValidator<ClusterPaymentProcessingConfig> validator;

        static ClusterPaymentProcessingConfig()
        {
            validator = new InlineValidator<ClusterPaymentProcessingConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class PersistenceConfig : IValidateable
    {
        static readonly InlineValidator<PersistenceConfig> validator;

        static PersistenceConfig()
        {
            validator = new InlineValidator<PersistenceConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class NetworkEndpointConfig : IValidateable
    {
        static readonly InlineValidator<NetworkEndpointConfig> validator;

        static NetworkEndpointConfig()
        {
            validator = new InlineValidator<NetworkEndpointConfig>
            {
                v => v.RuleFor(j => j.Host)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Host missing or empty"),

                v => v.RuleFor(j => j.Port)
                    .GreaterThan(0)
                    .WithMessage("Invalid port number '{PropertyValue}'"),
            };
        }
    }

    public partial class AuthenticatedNetworkEndpointConfig
    {
        public static readonly InlineValidator<AuthenticatedNetworkEndpointConfig> validator;

        static AuthenticatedNetworkEndpointConfig()
        {
            validator = new InlineValidator<AuthenticatedNetworkEndpointConfig>
            {
                v => v.RuleFor(j => j.Host)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Host missing or empty"),

                v => v.RuleFor(j => j.Port)
                    .GreaterThan(0)
                    .WithMessage("Invalid port number '{PropertyValue}'"),
            };
        }
    }

    public partial class EmailSenderConfig
    {
        static readonly InlineValidator<EmailSenderConfig> validator;

        static EmailSenderConfig()
        {
            validator = new InlineValidator<EmailSenderConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class AdminNotifications : IValidateable
    {
        static readonly InlineValidator<AdminNotifications> validator;

        static AdminNotifications()
        {
            validator = new InlineValidator<AdminNotifications>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class NotificationsConfig : IValidateable
    {
        static readonly InlineValidator<NotificationsConfig> validator;

        static NotificationsConfig()
        {
            validator = new InlineValidator<NotificationsConfig>
            {
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class ApiConfig : IValidateable
    {
        static readonly InlineValidator<ApiConfig> validator;

        static ApiConfig()
        {
            validator = new InlineValidator<ApiConfig>
            {
                v => v.RuleFor(j => j.ListenAddress)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("API listenAddress missing or empty"),

                v => v.RuleFor(j => j.Port)
                    .GreaterThan(0)
                    .WithMessage("Invalid API port number '{PropertyValue}'"),
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class PoolConfig : IValidateable
    {
        public static readonly InlineValidator<PoolConfig> validator;

        static PoolConfig()
        {
            validator = new InlineValidator<PoolConfig>
            {
                v => v.RuleFor(j => j.Id)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Pool id missing or empty"),

                v => v.RuleFor(j => j.Coin)
                    .NotNull()
                    .WithMessage("Coin config missing or empty"),

                v => v.RuleFor(j => j.Ports)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Stratum port config missing or empty"),

                v => v.RuleFor(j => j.Ports)
                    .Must((pc, ports, ctx)=>
                    {
                        if (ports.Keys.Any(port => port < 0))
                        {
                            ctx.MessageFormatter.AppendArgument("port", ports.Keys.First(port => port < 0));
                            return false;
                        }

                        return true;
                    })
                    .WithMessage("Invalid stratum port number {port}"),

                v => v.RuleFor(j => j.Address)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Pool wallet address missing or empty"),

                v => v.RuleFor(j => j.Daemons)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Pool daemons missing or empty"),

                v => v.RuleFor(j => j.Daemons)
                    .SetCollectionValidator(AuthenticatedNetworkEndpointConfig.validator),
            };
        }

        public void Validate()
        {
            this.Validate(validator);
        }
    }

    public partial class ClusterConfig : IValidateable
    {
        static readonly InlineValidator<ClusterConfig> validator;

        static ClusterConfig()
        {
            validator = new InlineValidator<ClusterConfig>
            {
                v => v.RuleFor(j => j.PaymentProcessing)
                    .NotNull(),

                v => v.RuleFor(j => j.Persistence)
                    .NotNull(),

                v => v.RuleFor(j => j.Pools)
                    .NotNull()
                    .NotEmpty(),

                // ensure pool ids are unique
                v => v.RuleFor(j => j.Pools)
                    .Must((pc, pools, ctx)=>
                    {
                        var ids = pools
                            .GroupBy(x => x.Id)
                            .ToArray();

                        if (ids.Any(id => id.Count() > 1))
                        {
                            ctx.MessageFormatter.AppendArgument("poolId", ids.First(id => id.Count() > 1).Key);
                            return false;
                        }

                        return true;
                    })
                    .WithMessage("Duplicate pool id '{poolId}'"),

                // ensure stratum ports are not assigned multiple times
                v => v.RuleFor(j => j.Pools)
                    .Must((pc, pools, ctx)=>
                    {
                        var ports = pools.SelectMany(x => x.Ports.Select(y => y.Key))
                            .GroupBy(x => x)
                            .ToArray();

                        foreach (var port in ports)
                        {
                            if (port.Count() > 1)
                            {
                                ctx.MessageFormatter.AppendArgument("port", port.Key);
                                return false;
                            }
                        }

                        return true;
                    })
                    .WithMessage("Stratum port {port} assigned multiple times"),

                v => v.RuleFor(j => j.Pools)
                    .SetCollectionValidator(PoolConfig.validator),
            };
        }

        public void Validate()
        {
            this.Validate(validator);

            // optional
            Api?.Validate();
            Logging?.Validate();
            Notifications?.Validate();

            // mandatory
            Persistence.Validate();
            PaymentProcessing.Validate();
        }
    }
}
