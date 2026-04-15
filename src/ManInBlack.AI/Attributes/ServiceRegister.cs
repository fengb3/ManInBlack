namespace ManInBlack.AI.Attributes;

public class ServiceRegister
{
    /// <summary>
    /// 注册为瞬态服务
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TransientAttribute : Attribute;

    public class Transient
    {
        /// <summary>
        /// 注册为瞬态服务，并指定服务类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [AttributeUsage(AttributeTargets.Class)]

        public class AsAttribute<T> : Attribute;
    }
    
    /// <summary>
    /// 注册为作用域服务
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ScopedAttribute : Attribute;

    public class Scoped
    {
        /// <summary>
        /// 注册为作用域服务，并指定服务类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [AttributeUsage(AttributeTargets.Class)]

        public class AsAttribute<T> : Attribute;
    }

    /// <summary>
    /// 注册为单例服务
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SingletonAttribute : Attribute;

    public class Singleton
    {
        /// <summary>
        /// 注册为单例服务，并指定服务类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [AttributeUsage(AttributeTargets.Class)]
        public class AsAttribute<T> : Attribute;
    }
}