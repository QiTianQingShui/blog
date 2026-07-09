
## 5. AIAgentHostExecutor




## 6. OutputMessagesExecutor 

在上面的示例中，我们利用创建的FunctionExecutor<ChatMessage>实现与LLM的交互，并通过调用IWorkflowContext的YieldOutputAsync方法将LLM的响应作为输出返回。OutputMessagesExecutor做了类似的事情，但是它输出的是ChatMessage列表。

```csharp
internal sealed class OutputMessagesExecutor : ChatProtocolExecutor, IResettableExecutor
{
	public OutputMessagesExecutor(ChatProtocolExecutorOptions? options = null)
		: base("OutputMessages", options, declareCrossRunShareable: true)
	{}
	protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
	    => base.ConfigureProtocol(protocolBuilder).YieldsOutput<List<ChatMessage>>();
	protected override ValueTask TakeTurnAsync(List<ChatMessage> messages, IWorkflowContext context, bool? emitEvents, CancellationToken cancellationToken = default)
	    => context.YieldOutputAsync(messages, cancellationToken);
	ValueTask IResettableExecutor.ResetAsync()=> default;
}
```

OutputMessagesExecutor一般会单独使用，当我们利用AgentWorkflowBuilder构建基于Agent的工作流时，内部会添加此节点来输出ChatMessage列表，所以它仅仅是一个内部类型。如上面的代码片段所示，OutputMessagesExecutor继承自ChatProtocolExecutor，意味着它具有在状态中累积ChatMessage列表的能力。它利用重写了TakeTurnAsync方法，在接收到TurnToken的时候通过调用IWorkflowContext的YieldOutputAsync方法输出累积的ChatMessage列表。

在如下所示的演示程序中，我们基于OpenAIClient创建了一个ChatClientAgent，并将其与OutputMessagesExecutor连接来构建Workflow。由于OutputMessagesExecutor指示一个内部类型，我们不得已采用反射的方式来创建它。ChatClientAgent响应携带的消息列表将会转发给OutputMessagesExecutor，并由它输出，所以我们可以通过提取最后一个WorkflowOutputEvent事件来获取这个消息列表，并通过打印最后一条消息来输出LLM的答复。

```csharp
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Reflection;

dotenv.net.DotEnv.Load();

var endpoint = Environment.GetEnvironmentVariable("OPENAI_URL")!;
var model = Environment.GetEnvironmentVariable("MODEL")!;
var apiKey = Environment.GetEnvironmentVariable("API_KEY")!;

var agent = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
    .GetChatClient(model)
    .AsIChatClient()
    .AsAIAgent();

var type = Assembly.Load(new AssemblyName("Microsoft.Agents.AI.Workflows"))
    .GetType("Microsoft.Agents.AI.Workflows.OutputMessagesExecutor")!;
var outputMessages = (Executor)Activator.CreateInstance(type, [null])!;

var workflow = new WorkflowBuilder(agent)
    .AddEdge(agent, outputMessages)
    .WithOutputFrom(outputMessages)
    .Build();

var run = await InProcessExecution.Default.RunAsync(workflow, new ChatMessage(ChatRole.User, "战国四大名将都是谁？"));
var messages = run.NewEvents.OfType<WorkflowOutputEvent>().Last().Data as IEnumerable<ChatMessage>;
Console.WriteLine(messages!.Last());
```

输出：

```markdown
战国四大名将是指中国战国时期四位杰出的军事将领，他们分别是：

1. **白起** - 秦国名将，以长平之战闻名，被誉为“人屠”。
2. **王翦** - 秦国名将，助秦始皇统一六国，战功赫赫。
3. **廉颇** - 赵国名将，以勇猛善战和负荆请罪的故事著称。
4. **李牧** - 赵国名将，长期抵御匈奴，并多次击败秦军。

这一说法主要来源于后世对战国时期杰出将领的总结。
```

##

```markdown
《诗经·卫风·氓》【第一章】氓之蚩蚩，抱布贸丝。匪来贸丝，来即我谋。送子涉淇，至于顿丘。匪我愆期，子无良媒。将子无怒，秋以为期。【第二章】乘彼垝垣，以望复关。不见复关，泣涕涟涟。既见复关，载笑载言。尔卜尔筮，体无咎言。以尔车来，以我贿迁。【第三章】桑之未落，其叶沃若。于嗟鸠兮，无食桑葚！于嗟女兮，无与士耽！士之耽兮，犹可说也；女之耽兮，不可说也。【第四章】桑之落矣，其黄而陨。自我徂尔，三岁食贫。淇水汤汤，渐车帷裳。女也不爽，士贰其行。士也罔极，二三其德。【第五章】三岁为妇，靡室劳矣；夙兴夜寐，靡有朝矣。言既遂矣，至于暴怒。兄弟不知，咥其笑矣。静言思之，躬自悼矣。【第六章】及尔偕老，老使我怨。淇则有岸，隰则有泮。总角之宴，言笑晏晏。信誓旦旦，不思其反。反是不思，亦已焉哉！
```