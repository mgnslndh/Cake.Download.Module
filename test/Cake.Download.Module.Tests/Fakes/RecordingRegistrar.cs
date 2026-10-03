using Cake.Core.Composition;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class RecordingRegistrar : ICakeContainerRegistrar
{
    public List<RecordingRegistration> Registrations { get; } = [];

    public ICakeRegistrationBuilder RegisterType(Type type)
    {
        var registration = new RecordingRegistration(type);
        Registrations.Add(registration);
        return registration;
    }

    public ICakeRegistrationBuilder RegisterInstance<TImplementation>(TImplementation instance)
        where TImplementation : class => RegisterType(typeof(TImplementation));
}

internal sealed class RecordingRegistration(Type implementation) : ICakeRegistrationBuilder
{
    public Type Implementation { get; } = implementation;

    public List<Type> Services { get; } = [];

    public bool IsSingleton { get; private set; }

    public ICakeRegistrationBuilder As(Type type)
    {
        Services.Add(type);
        return this;
    }

    public ICakeRegistrationBuilder AsSelf() => As(Implementation);

    public ICakeRegistrationBuilder Singleton()
    {
        IsSingleton = true;
        return this;
    }

    public ICakeRegistrationBuilder Transient()
    {
        IsSingleton = false;
        return this;
    }
}
