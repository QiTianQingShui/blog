

## 1. Context

```typescript
export interface Context {
  [symbols.isolate]: Dict<symbol>
  [symbols.intercept]: Dict
  root: this
  baseUrl?: string

  events: EventsService
  logger: LoggerService
  reflect: ReflectService
  registry: RegistryService
}
```

- `[symbols.isolate]`: 隔离域字典。决定哪些依赖或数据在当前上下文中是**隔离**的（不与父级或全局共享）。这在并行执行任务（例如多进程测试或多 Agent 并发调用）时非常关键，确保各任务之间的数据互不干扰。
- `[symbols.intercept]`: 拦截器/钩子字典。用于注册生命周期**拦截器或中间件**。框架可以通过它在执行某个动作（如调用服务、发送请求）的前后插入自定义逻辑。
- root: 指向根上下文的引用。在存在嵌套上下文（父子上下文）的架构中，子上下文可以通过 root 快速访问最顶层的全局配置或单例服务。使用 this 类型确保了类型链的一致性。
- baseUrl: 可选的网络或文件根路径（URL/Path）。通常用于 API 请求的基础路径、静态资源加载路径，或者远程服务调用的 Endpoint。

- events: 事件总线（Event Bus）。负责组件之间的异步解耦通信。通过发布/订阅模式（Publish/Subscribe），允许一个组件触发事件，其他组件监听并作出响应;
- logger: 日志服务。统一管理系统运行日志（Debug, Info, Warn, Error）。支持格式化输出、日志分级、输出到控制台或持久化到文件/云端;
- reflect: 反射与元数据服务（通常基于 reflect-metadata）。用于在运行时检查类型、读取装饰器（Decorators）附加的元数据。这在自动化依赖注入（DI）和动态组装组件时是必不可少的;
- registry: 注册中心。负责管理框架中所有可用的插件、自定义模型、Agent、工具（Tools）或配置项的注册与查找。它是系统扩展性的核心。

### 1.1 [symbols.isolate] & [symbols.intercept]

#### 1.1.1 [symbols.intercept]

symbols.intercept 对应的字典里存放的是针对各个依赖服务（Service）的“局部动态运行时配置（Scoped Runtime Configuration）”。简单来说，它存的不是服务实例本身，而是一份**参数修改说明书**。

##### 字典里究竟长什么样？

（内存结构）如果我们在代码里打印 ctx[symbols.intercept]，你会看到它是一个纯粹的键值对（Key-Value）映射表。

- 键（Key）：服务的名称（即 InjectKey，如 'logger'，'llm'，'http'）。
- 值（Value）：该服务专用的配置对象（结构必须符合服务自身通过 [symbols.config] 声明的类型）。

```typescript
// 假设我们在子上下文里做了拦截配置
const subCtx = ctx
  .intercept('logger', { level: 'debug', prepend: '[Agent-A]' })
  .intercept('llm', { temperature: 0.1 });

// 此时 subCtx[symbols.intercept] 的内存结构等同于：
{
  logger: { level: 'debug', prepend: '[Agent-A]' },
  llm: { temperature: 0.1 }
}
```

##### 它在底层是如何生效的？

（Proxy 联动机制）在前面我们分析的 Proxy get 拦截器中，当有代码访问 `ctx.logger` 时，框架会触发一套AOP逻辑。虽然全局可能只有唯一一个 LoggerService 实例（单例），但当不同的子上下文调用它时，Proxy 会动态地把当前上下文对应的拦截字典传给它：

```typescript
// 简化版的底层逻辑
get(target, prop, ctx) {
  const impl = findServiceImplementation(prop); // 找到真实的全局服务实例
  
  // 核心：从当前 ctx 的原型链上获取该服务的拦截配置
  const configOverride = ctx[symbols.intercept]?.[prop]; 
  
  if (configOverride) {
    // 如果有拦截配置，框架会用这个配置动态地“改造”或“包裹”这个服务实例
    // 例如返回一个专属的、应用了新配置的 Proxy 实例给当前调用者
    return createScopedServiceProxy(impl, configOverride);
  }
  
  return impl; // 没拦截，直接返回全局实例
}
```

由于 symbols.intercept 字典是用 Object.create(this[symbols.intercept]) 创建的，子上下文的字典会**以父上下文的字典为原型（Prototype）**。这意味着：

- 如果子上下文没配置 llm，它会沿着原型链自动向父级查找 llm 的拦截配置。
- 如果子上下文配置了 llm，它会直接覆盖父级的拦截配置，但绝对不会影响父级本身。

##### 它在 AI 框架（DeepSeek Harness）中的核心价值

在开发复杂的 AI 系统时，这个字典解决了**单例服务**与**多任务差异化需求**之间的冲突。

典型场景：LLM 服务路由与参数干预

假设系统中只有一个全局的 LLMService（负责处理 API 令牌、计费、网络重试和底层通信）。

- 路由 Agent A（写代码）：需要极高的确定性。
- 路由 Agent B（写小说）：需要极高的创造力。

如果直接修改全局服务的 temperature，两者就会互相打架。有了 intercept 字典，我们可以在不改变、不重新实例化 LLMService 的前提下，做局部行为干预：

```typescript
// 为代码 Agent 创建局部上下文
const codingCtx = ctx.intercept('llm', { 
  temperature: 0.0, 
  model: 'deepseek-coder' 
});
// 此时 codingCtx[symbols.intercept]['llm'] 存入了这行配置
agentA.run(codingCtx); 
// 当 agentA 内部调用 ctx.llm.chat() 时，拿到的服务会自动应用 0.0 的低温度和 coder 模型


// 为创意 Agent 创建另一个局部上下文
const creativeCtx = ctx.intercept('llm', { 
  temperature: 1.2, 
  model: 'deepseek-chat' 
});
// 此时 creativeCtx[symbols.intercept]['llm'] 存入了另一行配置
agentB.run(creativeCtx);
// 当 agentB 内部调用 ctx.llm.chat() 时，拿到的服务会自动应用 1.2 的高温度
```

symbols.intercept 字典存放的是一张局部的**参数微调清单**。它使得整个框架的单例服务（Service）具有了空间多态性——同一个服务实例，在不同的 Context 空间里，可以通过读取这张清单，表现出完全不同的运行参数和行为特征。


#### 1.1.2 [symbols.isolate]

symbols.isolate 对应的字典里存放的是**服务名称**到**隔离网格标签（Scope Label）**的映射表。大白话来说：intercept 字典里存的是参数说明书（怎么运行），而 isolate 字典里存的是**空间路标**（去哪里找实例）。它直接决定了当前上下文（Context）在访问某个服务时，究竟应该路由到哪一个独立的实例生存空间（Scope）。

##### 字典里究竟长什么样？（内存结构）

如果我们在代码里打印 ctx[symbols.isolate]，它是一个极其纯粹的、以 Symbol 为值的映射表：

- 键（Key）：服务的名称（字符串，如 'database'，'logger'）；
- 值（Value）：一个全局唯一的 Symbol 对象（也就是这个服务的隔离标签）。

实际代码示例：回忆一下之前我们分析的 isolate 源码：

```typescript
isolate(name: string, label?: symbol) {
  const shadow = Object.create(this[symbols.isolate])
  shadow[name] = label ?? Symbol(name) // 👈 核心：生成或绑定一个唯一的 Symbol 标签
  return this.extend({ [symbols.isolate]: shadow })
}
```

当我们调用 const subCtx = ctx.isolate('logger') 时，subCtx[symbols.isolate] 的内存结构等同于：

```typescript
{
  // 继承自父级的其他服务保持不变（通过原型链继承）
  database: Symbol.for('cordis.base.database'), 
  
  // 唯独 logger 被改写为了一个全新且独一无二的局部 Symbol
  logger: Symbol('logger') 
}
```

##### 它在底层是如何控制服务路由的？

我们结合前文分析的 Proxy get 拦截器中的那段核心循环（while (true)）来看：

```typescript
// 1. 拿到当前服务名（prop）在当前上下文中的隔离标签（也就是从 isolate 字典里读取）
const key = target[symbols.isolate][prop]

let fiber = ctx.fiber
while (true) {
  // 2. 去当前插件网格（fiber）的 store 里找：有没有绑定到这个具体 key（Symbol）的实例
  const impl = fiber.store?.[prop]
  if (impl) return getTraceable(ctx, impl.value)
  
  // 3. 【最关键的边界守卫】：如果当前层级找不到，准备向父级 fiber 向上攀爬
  // 如果父级的隔离标签和当前上下文的隔离标签不一致，说明“被隔离了”，立刻熔断报错！
  if (fiber.parent[symbols.isolate][prop] !== key) throw error 
  
  fiber = fiber.parent.fiber // 标签相同，说明是共享的，继续向上一层寻找
}
```

路由查找的两种结果：

- 没有调用 isolate('logger')：子上下文的 isolate 字典通过原型链直接读取**父级的 logger 标签**。在向上寻找服务时，fiber.parent[symbols.isolate]['logger'] === key 完全成立，于是可以一路向上追溯，最终拿到全局共享的同一个 Logger 实例；
- 调用了 isolate('logger')：子上下文的 isolate 字典里，logger 的键值被替换成了一个全新的 Symbol('logger')。当代码尝试向上追溯时，由于父级的标签和这个新 Symbol 不相等，路由链条在第一层就被强行斩断。它只能在当前**子上下文的 fiber.store** 里寻找你为它单独注入（provide）的新 Logger 实例。


### 1.2 Context class

```typescript
export class Context {
  static readonly effect: unique symbol = symbols.effect
  static readonly filter: unique symbol = symbols.filter
  static readonly isolate: unique symbol = symbols.isolate
  static readonly intercept: unique symbol = symbols.intercept
  
  //确定指定的value是否是一个Conext
  static is(value: any): value is Context {
    return !!value?.[Context.is as any]
  }
  static {
    Context.is[Symbol.toPrimitive] = () => Symbol.for('cordis.is')
    Context.prototype[Context.is as any] = true
  }

  constructor() {
    this[symbols.isolate] = Object.create(null)
    this[symbols.intercept] = Object.create(null)
    const self = new Proxy<this>(this, ReflectService.handler)
    this.root = self
    this.baseUrl = undefined
    this.fiber = new Fiber(self, {}, Object.create(null), null, () => [])
    this.reflect = new ReflectService(self)
    this.registry = new RegistryService(self)
    this.events = new EventsService(self)
    this.logger = new LoggerService(self)
    this.fiber._disposables.clear()
    return self
  }

  // 根据指定的元数据创建一个子Context
  // 如果不需要覆盖，则不需要创建
  extend(meta = {}): this {
    const shadow = Reflect.getOwnPropertyDescriptor(this, symbols.shadow)?.value
    const self = Object.create(getTraceable(this, this))
    for (const prop of Reflect.ownKeys(meta)) {
      Object.defineProperty(self, prop, Reflect.getOwnPropertyDescriptor(meta, prop)!)
    }
    if (!shadow) return self
    return Object.assign(Object.create(self), { [symbols.shadow]: shadow })
  }


  // 创建一个子上下文， 并在其中隔离特定的服务（Service）
  isolate(name: string, label?: symbol) {

    // 创建了一个新的字典 shadow，它继承了父级所有的隔离规则
    const shadow = Object.create(this[symbols.isolate])

    //在新的字典中，为名为 name 的服务重新指定（或者生成一个全新的）Symbol 作为其隔离标签。
    shadow[name] = label ?? Symbol(name)
    return this.extend({ [symbols.isolate]: shadow })
  }

  //isolate 非常相似，都是通过“原型链继承 + 覆盖特定 Symbol”的模式来实现局部覆盖，不污染父级。
  intercept<K extends InjectKey>(name: K, config: Context[K] extends { [symbols.config]: infer T } ? T : never): this
  intercept(name: string, config: any): this
  intercept(name: string, config: any) {
    const intercept = Object.create(this[symbols.intercept])
    intercept[name] = config
    return this.extend({ [symbols.intercept]: intercept })
  }
}

export type InjectKey = keyof {
  [K in keyof Context & string as Context[K] extends { [symbols.config]: any } ? K : never]: any
}
```

### 1.3 ReflectService.Handler

```typescript
export class ReflectService {
  /** Proxy traps implementing service resolution for every context object. */
  static handler: ProxyHandler<Context> = {
    get: (target, prop, ctx: Context) => {
    
      // 特殊属性，直接返回目标对象同名属性
      // 逃生舱口（Special Property）：像 toString、then 或内置的 Symbol 等特殊属性，不属于依赖注入管辖，直接放行，避免破坏 JavaScript 的基础语言特性。
      if (isSpecialProperty(prop)) {
        return Reflect.get(target, prop, ctx)
      }

      // 如果目标对象包含此属性，进行Traceable封装
      // 目的：确保无论这个服务被传递到哪里、被谁调用，服务内部在访问上下文时，都能追踪到当前发起调用的正是这个 ctx，实现调用栈的上下文联动。
      if (Reflect.has(target, prop)) {
        return getTraceable(ctx, Reflect.get(target, prop, ctx))
      }

      //如果走到这一步，说明用户正在尝试访问一个 Context 本身没有的属性（例如用户写了 ctx.myCustomService），接下来框架进入极其严格的“依赖注入权限审查”：
      // 核心铁律：在现代组件化/插件框架中，“未声明，禁止使用”。
      // 如果你没有在当前插件的 inject 列表中声明你需要 myCustomService，那么即便这个服务在全局是存在的，框架也会准备好抛出这个错误。
      // 这强迫开发者编写高内聚、声明清晰的插件。
      const error = new Error(`cannot get property "${prop}" without inject`)

      try {

        // 如果是getter
        const def = target.reflect.props[prop]
        if (def?.type === 'accessor') {
          return def.get.call(ctx, ctx[symbols.receiver], error)
        }

        if (!ctx.fiber.runtime) return ctx.reflect.get(prop, false)
        return ctx.events.waterfall('internal/get', ctx, prop, error, () => {
          const key = target[symbols.isolate][prop]
          let fiber = (ctx[symbols.shadow] as Context ?? ctx).fiber

          // 动态路由与隔离回溯（网络/骨架层）
          // while (true) 的瀑布流（Waterfall）和 Fiber 回溯中，它揭示了 symbols.isolate 和 symbols.shadow 是如何配合 Proxy 落地实现的：
          while (true) {
            const impl = fiber.store?.[prop]
            if (impl) return getTraceable(ctx, impl.value)
            if (prop in fiber.inject) {
              error.message = `cannot get required service "${prop}" in inactive context`
              throw error
            }
            if (!fiber.runtime) throw error
            if (fiber.parent[symbols.isolate][prop] !== key) throw error
            fiber = fiber.parent.fiber
          }
        })
      } catch (e: any) {
        throw e === error ? enhanceError(e) : e
      }
    },

    set: (target, prop, value, ctx: Context) => {
      if (isSpecialProperty(prop)) {
        return Reflect.set(target, prop, value, ctx)
      }

      const error = new Error(`cannot set property "${prop}" without provide`)
      const def = target.reflect.props[prop]
      if (!def) {
        if (!ctx.fiber.runtime) return Reflect.set(target, prop, value, ctx)
        throw enhanceError(error)
      }

      try {
        if (def.type === 'accessor') {
          if (!def.set) return false
          return def.set.call(ctx, value, ctx[symbols.receiver], error)
        }

        return ctx.events.waterfall('internal/set', ctx, prop, value, error, () => {
          return ctx.reflect.set(prop, value, error)
        })
      } catch (e: any) {
        throw e === error ? enhanceError(e) : e
      }
    },

    has: (target, prop) => {
      if (isSpecialProperty(prop)) {
        return Reflect.has(target, prop)
      }
      if (Reflect.has(target, prop)) return true
      return !!target.reflect.props[prop]
    },
  }
}
```

这段 Proxy 路由机制回答了以下三个设计问题：

- **怎么做到“局部覆盖”（Isolate）？**如果我们在子上下文中调用了 ctx.isolate('db')，target[symbols.isolate]['db'] 就会变成一个新的 Symbol。当沿着父级链条向上找时，一比对 if (fiber.parent[symbols.isolate]['db'] !== key)，发现标签不一样，循环立刻熔断并报错。这就成功把父级的服务屏蔽掉了。
- **怎么做到“未激活阻断”？**如果服务存在，但它的生命周期还没走到 active（可能还在异步初始化中），Proxy 会在 if (prop in fiber.inject) 处拦截，抛出 inactive context 错误，避免代码在服务未准备就绪时运行。
- **怎么做到动态拦截（Waterfall）？**ctx.events.waterfall('internal/get', ...) 允许其他第三方插件通过**事件**，动态地给当前属性注入值。这意味着这个 Proxy 系统甚至允许“动态劫持”。


### 1.4 isSpecialProperty

```typescript
// - is a symbol
// - is a reserved word (prototype, then)
// - is a number string (0, 1, 2, ...)
// - starts with `_`
function isSpecialProperty(prop: string | symbol): prop is symbol {
  return typeof prop === 'symbol'
    || RESERVED_WORDS.includes(prop)
    || parseInt(prop).toString() === prop
    || prop.startsWith('_')
}
```

### 1.5 getTraceable

```typescript
//Wrap services/functions so method calls see the caller's active context. */
export function getTraceable<T>(ctx: Context, value: T): T {

  // 如果传入的 value 只是普通的字符串、数字、布尔值，根本不需要追踪，直接原样返回。只有对象、函数、服务实例才需要处理。
  if (!isObject(value)) return value

  // 如果检测到传入的 value 已经是被包装过的“影子外壳”（身上带有 symbols.shadow 属性），说明它被套过娃了。
  // 为了防止无限包裹导致性能下降，框架选择“脱壳”，直接通过 Object.getPrototypeOf(value) 拿到它的上一层原型（即真实的原始服务对象）。
  if (Object.hasOwn(value, symbols.shadow)) {
    return Object.getPrototypeOf(value)
  }

  // 并非所有的对象都需要上下文追踪。
  // 服务类通常会在其原型或构造时，挂载一个特殊的 symbols.tracker 属性（这个属性通常是一个配置对象，指定了哪些方法需要被拦截和追踪）。
  // 如果服务没有声明 tracker，说明它不需要感知上下文，直接放行。
  const tracker = value[symbols.tracker]
  if (!tracker) return value

  //当上述条件都满足，框架调用内部的 createTraceable。
  // 它通常会使用 Proxy 或者 原型链扭曲（Object.create 加上一层 Getter/Setter），把原始的 value（服务实例）包裹成一个全新的临时外壳对象。
  // 这个外壳的核心使命是：把当前的 ctx（调用者的上下文）强行绑定到这次调用的生命周期中。
  return createTraceable(ctx, value, tracker)
}
```

这个函数 getTraceable 是整个框架用来实现“上下文穿透与追踪（Context Tracking）”的灵魂所在。正如它的代码注释所说：“包裹服务或函数，使得方法调用时，能够看到调用者当前处于激活状态的上下文（Context）。”

在传统的面向对象（OOP）中，一个服务实例内部的 this.ctx 在创建时就固定了。但在支持并发、多 Agent 异步调用的 AI 框架中，一个全局单例服务（比如 LLMService）会同时被 Agent A、Agent B 和 Agent C 调用。当服务的方法被执行时，它怎么知道现在是哪个 Agent 在叫它，进而去读取那个 Agent 专属的隔离配置（intercept）或环境数据呢？getTraceable 就是为了解决这个“谁在调用我”的痛点而设计的。

### 1.6 createTraceable

```typescript
function createTraceable(ctx: Context, value: any, tracker: Tracker) {
  // noShadow services are identity-aware (e.g. logger uses the origin fiber to
  // derive its name): keep the shadow ctx so they can read [symbols.shadow]
  // and resolve the origin. Non-noShadow services strip — their side effects
  // bind to caller, not origin.
  if (ctx[symbols.shadow] && !tracker.noShadow) {
    ctx = Object.getPrototypeOf(ctx)
  }
  const proxy = new Proxy(value, {
    get: (target, prop, receiver) => {
      if (prop === symbols.original) return target
      if (prop === tracker.property) return ctx
      if (typeof prop === 'symbol') {
        return Reflect.get(target, prop, receiver)
      }
      if (tracker.associate && ctx.reflect.props[`${tracker.associate}.${prop}`]) {
        return Reflect.get(ctx, `${tracker.associate}.${prop}`, withProp(ctx, symbols.receiver, receiver))
      }
      let shadow: any, innerValue: any
      const desc = getPropertyDescriptor(target, prop)
      if (desc && 'value' in desc) {
        innerValue = desc.value
      } else {
        shadow = createShadow(ctx, target, tracker.property, receiver)
        innerValue = Reflect.get(target, prop, shadow)
      }
      const innerTracker = innerValue?.[symbols.tracker]
      if (innerTracker) {
        return createTraceable(ctx, innerValue, innerTracker)
      } else if (!tracker.noShadow && typeof innerValue === 'function') {
        shadow ??= createShadow(ctx, target, tracker.property, receiver)
        return createShadowMethod(ctx, innerValue, receiver, shadow)
      } else {
        return innerValue
      }
    },
    set: (target, prop, value, receiver) => {
      if (prop === symbols.original) return false
      if (prop === tracker.property) return false
      if (typeof prop === 'symbol') {
        return Reflect.set(target, prop, value, receiver)
      }
      if (tracker.associate && ctx.reflect.props[`${tracker.associate}.${prop}`]) {
        return Reflect.set(ctx, `${tracker.associate}.${prop}`, value, withProp(ctx, symbols.receiver, receiver))
      }
      const shadow = createShadow(ctx, target, tracker.property, receiver)
      return Reflect.set(target, prop, value, shadow)
    },
    apply: (target, thisArg, args) => {
      return applyTraceable(proxy, target, thisArg, args)
    },
  })
  return proxy
}
```