// using System.ComponentModel.DataAnnotations;
// using System.Diagnostics;
// using System.Linq.Expressions;
// using System.Reflection;
// using FastExpressionCompiler;
// using Masuit.Tools.Core.AspNetCore;
// using Microsoft.EntityFrameworkCore;
// // using Masuit.Tools.Core.AspNetCore;
//
// namespace DifyRawBot.Helper;
//
// public static class DatabaseHelper
// {
//     /// <summary>
//     /// 更新数据库到最新迁移状态
//     /// </summary>
//     /// <param name="app"></param>
//     /// <typeparam name="TDbContext"></typeparam>
//     /// <returns></returns>
//     public static WebApplication UpdateDatabase<TDbContext>(this WebApplication app)
//         where TDbContext : DbContext
//     {
//         using var scope     = app.Services.CreateScope();
//         using var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
//         dbContext?.Database.Migrate();
//         return app;
//     }
//
//
//     /// <summary>
//     /// 添加或更新实体，并返回被添加或更新的对象实例
//     /// </summary>
//     /// <typeparam name="T">实体类型</typeparam>
//     /// <typeparam name="TKey">更新依据的字段类型</typeparam>
//     /// <param name="dbSet">目标DbSet</param>
//     /// <param name="keySelector">用于确定更新依据的字段表达式</param>
//     /// <param name="entities">待处理的实体集合</param>
//     /// <param name="addedEntities"></param>
//     /// <param name="updatedEntities"></param>
//     /// <param name="ignoreNavigationProperty">是否忽略导航属性</param>
//     /// <returns>被添加或更新的实体集合</returns>
//     public static IEnumerable<T> AddOrUpdate<T, TKey>(
//         this DbSet<T>             dbSet,
//         Expression<Func<T, TKey>> keySelector,
//         IEnumerable<T>            entities,
//         out ICollection<T>        addedEntities,
//         out ICollection<T>        updatedEntities,
//         bool                      ignoreNavigationProperty = false) where T : class
//     {
//         ArgumentNullException.ThrowIfNull(keySelector);
//         ArgumentNullException.ThrowIfNull(entities);
//
//         addedEntities   = [];
//         updatedEntities = [];
//
//         var collection = entities as ICollection<T> ?? entities.ToList();
//         if (collection.Count == 0)
//         {
//             return [];
//         }
//
//         var func           = keySelector.CompileFast();
//         var keyObjects     = collection.Select(func).ToList();
//         var parameter      = keySelector.Parameters[0];
//         var array          = Expression.Constant(keyObjects);
//         var containsMethod = typeof(List<TKey>).GetMethod(nameof(List<TKey>.Contains));
//         var call           = Expression.Call(array, containsMethod, keySelector.Body);
//         var lambda         = Expression.Lambda<Func<T, bool>>(call, parameter);
//         var items          = dbSet.Where(lambda).ToDictionary(func);
//
//         foreach (var entity in collection)
//         {
//             var key = func(entity);
//             if (items.TryGetValue(key, out var existingEntity))
//             {
//                 var dataType = typeof(T);
//                 var keyIgnoreFields = dataType.GetProperties()
//                     .Where(p => p.GetCustomAttribute<KeyAttribute>() != null ||
//                                 p.GetCustomAttribute<UpdateIgnoreAttribute>() != null)
//                     .ToList();
//
//                 if (!keyIgnoreFields.Any())
//                 {
//                     string idName = dataType.Name + "Id";
//                     keyIgnoreFields.AddRange(dataType.GetProperties()
//                         .Where(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
//                                     p.Name.Equals(idName, StringComparison.OrdinalIgnoreCase)));
//                 }
//
//                 if (ignoreNavigationProperty)
//                 {
//                     keyIgnoreFields.AddRange(dataType.GetProperties()
//                         .Where(p => p.PropertyType.Namespace == "System.Collections.Generic"));
//                 }
//
//                 // 更新非忽略字段
//                 foreach (var property in typeof(T).GetProperties()
//                              .Where(p => p.CanRead && p.CanWrite && !keyIgnoreFields.Any(f => f.Name == p.Name)))
//                 {
//                     var newValue      = property.GetValue(entity);
//                     var existingValue = property.GetValue(existingEntity);
//                     if (!Equals(existingValue, newValue))
//                     {
//                         property.SetValue(existingEntity, newValue);
//                     }
//                 }
//
//                 // 回写主键字段（确保传入实体的主键与数据库一致）
//                 foreach (var keyField in keyIgnoreFields.Where(p => p.CanRead && p.CanWrite))
//                 {
//                     var dbValue = keyField.GetValue(existingEntity);
//                     keyField.SetValue(entity, dbValue);
//                 }
//
//                 updatedEntities.Add(existingEntity);
//             }
//             else
//             {
//                 dbSet.Add(entity);
//                 addedEntities.Add(entity);
//             }
//         }
//
//         return addedEntities.Concat(updatedEntities);
//     }
//
//
//     /// <summary>
//     /// 同步数据库实体与目标集合，支持自定义忽略更新的属性
//     /// </summary>
//     /// <typeparam name="T">实体类型</typeparam>
//     /// <typeparam name="TKey">主键类型</typeparam>
//     /// <param name="dbSet">数据库实体集合</param>
//     /// <param name="selector">筛选数据库实体的条件</param>
//     /// <param name="keySelector">主键选择器</param>
//     /// <param name="target">目标集合</param>
//     /// <param name="added">新增的实体集合</param>
//     /// <param name="updated">更新的实体集合</param>
//     /// <param name="deleted">删除的实体集合</param>
//     /// <param name="ignoreProperties">需要忽略更新的属性表达式（如 e => e.ModifiedTime）</param>
//     public static void SyncWithTarget<T, TKey>(
//         this DbSet<T>                         dbSet,
//         Expression<Func<T, bool>>             selector,
//         Expression<Func<T, TKey>>             keySelector,
//         ICollection<T>                        target,
//         out    ICollection<T>                 added,
//         out    ICollection<T>                 updated,
//         out    ICollection<T>                 deleted,
//         params Expression<Func<T, object?>>[] ignoreProperties) where T : class where TKey : notnull
//     {
//         added   = new List<T>();
//         updated = new List<T>();
//         deleted = new List<T>();
//
//         // 获取需要忽略更新的属性名
//         var ignoredPropNames = GetPropertyNamesFromExpressions(ignoreProperties!);
//
//         // 获取数据库现有实体
//         var keyExtractor = keySelector.CompileFast();
//         var dbEntities   = dbSet.Where(selector).ToDictionary(keyExtractor);
//         var targetDict   = target.ToDictionary(keyExtractor);
//
//         // 处理删除：存在于数据库但不在目标集合中的实体
//         foreach (var dbEntity in dbEntities.Values)
//         {
//             var key = keyExtractor(dbEntity);
//             if (!targetDict.ContainsKey(key))
//             {
//                 dbSet.Remove(dbEntity);
//                 deleted.Add(dbEntity);
//             }
//         }
//
//         // 处理新增和更新
//         foreach (var targetEntity in target)
//         {
//             var key = keyExtractor(targetEntity);
//             if (dbEntities.TryGetValue(key, out var dbEntity))
//             {
//                 // 更新实体（排除导航属性、主键和自定义忽略属性）
//                 if (UpdateEntity(dbEntity, targetEntity, ignoredPropNames))
//                 {
//                     updated.Add(dbEntity);
//                 }
//             }
//             else
//             {
//                 dbSet.Add(targetEntity);
//                 added.Add(targetEntity);
//             }
//         }
//     }
//
//     /// <summary>
//     /// 更新实体属性（排除导航属性、主键和自定义忽略属性）
//     /// </summary>
//     private static bool UpdateEntity<T>(
//         T               existingEntity,
//         T               newEntity,
//         HashSet<string> ignoredPropNames)
//     {
//         var isUpdated = false;
//
//         var properties = typeof(T).GetProperties()
//             .Where(p => p.CanRead && p.CanWrite)
//             .Where(p => !IsNavigationProperty(p)
//                         && !IsKeyProperty(p)
//                         && !ignoredPropNames.Contains(p.Name));
//
//         foreach (var property in properties)
//         {
//             var newValue      = property.GetValue(newEntity);
//             var existingValue = property.GetValue(existingEntity);
//             if (Equals(existingValue, newValue)) continue;
//
//             isUpdated = true;
//             property.SetValue(existingEntity, newValue);
//         }
//
//         return isUpdated;
//     }
//
//     /// <summary>
//     /// 从表达式树中提取属性名
//     /// </summary>
//     private static HashSet<string> GetPropertyNamesFromExpressions<T>(
//         params Expression<Func<T, object>>[] expressions)
//     {
//         var names = new HashSet<string>();
//         foreach (var expr in expressions)
//         {
//             switch (expr.Body)
//             {
//                 case MemberExpression memberExpr:
//                     names.Add(memberExpr.Member.Name);
//                     break;
//                 case UnaryExpression { Operand: MemberExpression unaryMemberExpr }:
//                     // 处理值类型被装箱的情况（如 e => e.IntProperty）
//                     names.Add(unaryMemberExpr.Member.Name);
//                     break;
//             }
//         }
//
//         return names;
//     }
//
//     /// <summary>
//     /// 判断是否为导航属性
//     /// </summary>
//     private static bool IsNavigationProperty(PropertyInfo p)
//     {
//         Debug.Assert(p.PropertyType.Namespace != null, "p.PropertyType.Namespace != null");
//
//         // var callerNamespace = p.DeclaringType?.Namespace;
//
//         return p.PropertyType.Namespace == typeof(ICollection<>).Namespace
//                || p.PropertyType.Namespace.StartsWith("AlertSuggestion")
//                || p.PropertyType.Assembly == typeof(DbContext).Assembly;
//     }
//
//     /// <summary>
//     /// 判断是否为主键属性
//     /// </summary>
//     private static bool IsKeyProperty(PropertyInfo p)
//     {
//         return p.GetCustomAttribute<KeyAttribute>() != null;
//     }
// }