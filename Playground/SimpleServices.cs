using ManInBlack.AI.Attributes;

namespace Playground;

// --- 瞬态 ---

[ServiceRegister.Transient]
public class ServiceShouldRegisteredAsTransient;

[ServiceRegister.Transient.As<IServiceShouldRegisteredAsTransient>]
public class ServiceShouldRegisteredAsTransientWithInterface : IServiceShouldRegisteredAsTransient;

public interface IServiceShouldRegisteredAsTransient;


// --- 作用域 ---
[ServiceRegister.Scoped]
public class ServiceShouldRegisteredAsScoped;

[ServiceRegister.Scoped.As<IServiceShouldRegisteredAsScoped>]
public class ServiceShouldRegisteredAsScopedWithInterface : IServiceShouldRegisteredAsScoped;

public interface IServiceShouldRegisteredAsScoped;


// --- 单例 ---
[ServiceRegister.Singleton]
public class ServiceShouldRegisteredAsSingleton;

[ServiceRegister.Singleton.As<IServiceShouldRegisteredAsSingleton>]
public class ServiceShouldRegisteredAsSingletonWithInterface : IServiceShouldRegisteredAsSingleton;

public interface IServiceShouldRegisteredAsSingleton;