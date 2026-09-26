DSH本质上Agent开发框架，同时也作为Agent的执行引擎，在我看来和其他Agent开发框架在本质上并无不同。原因很简单，**Agent = LLM + Harness**，LLM是Agent的大脑，Harness是指导、规范和约束大脑，使它按照你希望的方式进行推理，并最终解决你提出问题的手段。Agent需要适配主流的LLM，Harness手段也不会依赖于你采用的Agent开发平台，它本质上是一种通用的手段。DSH仅仅是基于现有的LLM交互方式和这些通用的Harness手段提供的另一种解决方案而已。

针对一次针对Agent的调用，一般会涉及以调用LLM为主导的一系列处理流程。从软件设计的角度来看，这套处理可以采用不同的方式来设计，比如**工作流**、**状态机**抑或是**Actor模型**。而且一项任务会涉及针对同一**语境**下针对Agent的多次调用，这里所谓的**语境**又被称为**上下文（Context）**、**会话（Session）**或者**对话（Conversation）**。由于LLM和HTTP一样采用**无状态**的消息交换模式，语境的最终实现最终都会转换成作为LLM输入的完整或者经过裁剪的**对话历史**。

对于上面针对Agent处理流程的构建和执行，不同的Agent平台采用不同的实现方式。比如LangGraph在设计使采用有状态的有向有环图，并在运行时将其转换成Actor模型。LangChain在此基础上构建了基于**LLM + Tools**双节点状态图实现了基于工具选择执行的ReAct循环，并利用中间件的方式实现流程处理节点的动态添加和针对模型和工具调用的动态拦截，相应的Harness手段可以借助于注册的中间件应用到状态图中。DeepAgents作为LangChain平台Harness框架，本质上就是对一组预定义中间件的组合。

与LangChain殊途同归(不论采用何种构建方式，Agent最终体现为通过LangGraph构建的状态图，在运行时体现为被称为Pregel的Actor模型)不同的，MAF（Microsoft Agent Framework）走了两条岔路。一方面它利用工作流提供了针对任意结构处理流程的定制，在构建上虽然类似于LangGraph的状态图，但是在运行实现上则是走的消息路由的路子，这与LangGraph采用基于Pregel的Actor模型完全不同。在另一方面又为典型的推理流程构建了Agent管道（可以将图视为三维，管道视为二维），整个管道由一系列扩展组件组成，包括三种类型的中间件（Agent中间件、ChatClient中间件和AIFunction中间件）、用来提供对话历史的ChatHistoryProvider和用来提供输入输出增强的AIContextProvider，最终利用IChatClient对象于不同类型的LLM连接在一起。ReAct循环以及各种Harness手段都可以实现在对应的管道组件上。MAF将常规的Harness手段实现在一系列的管道组件中，并且提供了HarnessAgent这个Agent中间件将它们整合在一起。

DSH又是怎么玩的呢？我们已经知道了它的基石是Cordis，这是一个基于插件的开发和执行框架。一个Agent本质上就是具有树形层次结构的Context树，这样的结构不仅促成了功能的复用（子Context通过继承父Context的服务实现了子承父业）和执行的隔离（子Context可以重新注册服务实现），还真正实现了支持热拔插的插件系统（某个插件附着于某个Context，并利用专属的Fiber进行生命周期管理）。虽然插件是独立部署的基本单元，但并不意味着在插件本身在业务功能的执行上具有独立性。正好相反，插件一般情况下会依赖另一个插件提供的服务，所以插件自身的可用性会随着其他插件的上下线而改变。Cordis一方面直接利用当前维护的Fiber直接改变相关插件的状态，还利用事件总线让各个模块可以采用一种松耦合的方式整合在一起。

那么利用DSH开发的Agent具体是如何与Cordis扯上关系的呢？其实很简单，DSH自身会利用注册的原生插件提供一些基础服务（比如针对不同厂商的模型适配服务，工具注册服务、会话管理服务、存储与记忆等等），我们可以利用多层次的配置系统通过注册相应的插件来提供工具，以及完成实现在LangChain或者MAF各种中间件和扩展组件的Harness手段。与此同时，借助Cordis提供的事件总线保证了插件为了能够实现热拔插的独立性，同时为它们提供相互通信的基础设施。

以ReAct循环为例。LangChain和MAF在实现ReAct时，本质上是一个封装好的、处于统治地位的**中心化引擎**，由它来点名调用各个函数。但在以 Cordis 为底座的 DSH 中，根本没有一个居高临下的引擎。在 Cordis 层面，并不存在一个包含 `while True` 的硬编码主函数。所谓的循环，是一组松散的插件在事件总线上玩的一场**接力赛**。


实现在EventsService的事件总线是Cordis的通信网络，也是DeepSeek Harness基于通信的基础设施。很多追捧者将它跨上了天，但是我个人却有不同的看法。首先事件总线对于Cordis这个插件内核来说是没有问题的，而且从事件总线和消息总线的层面来说，事件总线算是优秀的设计。当时DSH将Cordis作为核心引擎，并使用它来构建Agent，我认为是有问题的。由于LLM本质上是深度神经网络，本质上是概率模型。概率模型的不确定性决定了Agent的不确定性，客服Agent不确定的必要的手段就是让设计者或者开发随时对Agent内部的流程有清晰的认识，但是事件总线这瓶万能胶被到处倾倒，使我们几乎不可能做到这一点。

我们都知道DSH中一切皆插件，换言之插件可以干任何事。为了让独立部署并独自管理生命周期的插件能够相互通信，它们之间会通过事件的订阅产生千丝万缕的勾连。所以如想了解Agent真正的工作流程，原则上比必须了解所有插件

### 1.1 GenerateOptions

调用stream方法代表一次针对LLM的调用，作为输入的`GenerateOptions`是一个**单次模型请求**的完整参数对象，调用 LLM 时把所有需要的信息打包成一个`GenerateOptions`对象。可以看出GenerateOptions携带了标识LLM的提供者和名称，这是本文标题体现的针对LLM的动态路由。

```typescript
export interface GenerateOptions {
  provider: string
  model: string
  reasoningEffort?: ReasoningEffortId
  messages: Message[]
  system?: string
  tools?: ToolSchema[]
  temperature?: number
  maxTokens?: number
  stop?: string[]
  signal?: AbortSignal
  sessionId?: Branded<'SessionId'>
  purpose?: 'compaction' | 'session-title'
}
```

各种字段成员说明如下：

- **provider**：已注册的针对不同LLM提供商的路由名，用来选择具体的适配器实例；
- **model**：具体要调用的模型名；
- **reasoningEffort**：可选。针对当前这个模型选择的推理强度（由适配器自己定义可选值）；
- **messages**：作为核心输入的代表对话历史的消息列表，
- **system**：系统提示词；
- **tools**：可用工具的Schema；
- **temperature**：采样温度，控制随机性；
- **maxTokens**：最大生成 token 数；
- **stop**：停止序列。模型一旦生成其中任一字符串就立即停止，且该字符串本身不会出现在输出中；
- **signal**：用于取消/中止本次请求的信号；
- **sessionId**：将多次对话纳入同一个Session的ID；
- **purpose**：对辅助模型调用的、与provider无关的分类。适配器可据此映射为隐藏元数据或专门的生成策略。目前仅两种取值：
  - `'compaction'`：压缩（如上下文压缩）；
  - `'session-title'`：生成会话标题。

```typescript
export type ReasoningEffortId = Branded<'ReasoningEffortId'>
declare const BRAND: unique symbol
export type Branded<B extends string> = string & { readonly [BRAND]: B }
```