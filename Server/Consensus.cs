namespace Server;

public record NodeMessage(int Sender, int Recipient, string Type, Guid ProposalId, string? Value = null);
public record NodeStatus(int Id, string Status, string Role, bool Responding, bool VoteYes, string? Accepted, int[] Peers);
public record ClusterStatusView(NodeStatus[] Nodes, VoteResult? LastVote, string? Accepted);
public record VoteResult(string Proposal, int? Leader, int Yes, int No, int Majority, bool Achieved);

public sealed class ConsensusNode
{
    public int Id { get; }
    public bool Responding { get; set; } = true;
    public bool Available { get; set; } = true;
    public bool Leader { get; set; }
    public bool VoteYes { get; set; } = true;
    public string? Accepted { get; set; }
    public int[] Peers { get; }

    public ConsensusNode(int id, IEnumerable<int> peers)
    {
        Id = id;
        Peers = peers.Where(peer => peer != id).ToArray();
    }

    public NodeMessage? Receive(NodeMessage message)
    {
        if (message.Recipient != Id || (message.Sender != Id && !Peers.Contains(message.Sender)))
        {
            throw new ArgumentException("Некорректный отправитель или получатель");
        }
        if (message.Type is not ("HEARTBEAT" or "PROPOSE" or "COMMIT"))
        {
            throw new ArgumentException("Неизвестный тип сообщения");
        }
        if (message.Type != "HEARTBEAT" && (message.ProposalId == Guid.Empty || string.IsNullOrWhiteSpace(message.Value)))
        {
            throw new ArgumentException("Некорректное предложение");
        }
        if (!Responding) { return null; }
        if (message.Type == "COMMIT") { Accepted = message.Value; }
        string response = message.Type == "HEARTBEAT" ? "ALIVE" : message.Type == "PROPOSE" ? (VoteYes ? "YES" : "NO") : "ACK";
        return new NodeMessage(Id, message.Sender, response, message.ProposalId);
    }
}

public sealed class ConsensusCluster
{
    private readonly object gate = new();
    private readonly List<ConsensusNode> nodes;
    private readonly Action<string> log;
    private string? accepted;
    private VoteResult? last;

    public ConsensusCluster(Action<string>? log = null)
    {
        this.log = log ?? Console.WriteLine;
        int[] ids = Enumerable.Range(1, 5).ToArray();
        nodes = ids.Select(id => new ConsensusNode(id, ids)).ToList();
        Elect();
    }

    private void Log(string message) => log($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    private ConsensusNode Node(int id) => nodes.SingleOrDefault(n => n.Id == id)
        ?? throw new ArgumentException("Неизвестный ID узла");

    private NodeMessage? Send(NodeMessage message)
    {
        try
        {
            Node(message.Sender);
            ConsensusNode recipient = Node(message.Recipient);
            string proposalInfo = message.ProposalId == Guid.Empty ? string.Empty : $" {message.ProposalId}";
            Log($"Node {message.Sender} -> Node {message.Recipient}: {message.Type}{proposalInfo}");
            NodeMessage? response = recipient.Receive(message);
            if (response == null)
            {
                if (message.Type == "HEARTBEAT" && recipient.Available) { Thread.Sleep(250); }
                Log($"Node {message.Recipient}: {(message.Type == "HEARTBEAT" ? "HEARTBEAT TIMEOUT" : "NO RESPONSE")}");
                return null;
            }
            if (response.Sender != message.Recipient || response.Recipient != message.Sender || response.ProposalId != message.ProposalId)
            {
                throw new ArgumentException("Некорректный ответ узла");
            }
            Log($"Node {response.Sender} -> Node {response.Recipient}: {response.Type}");
            return response;
        }
        catch (ArgumentException ex)
        {
            Log($"Ошибка сообщения: {ex.Message}");
            return null;
        }
    }

    public ClusterStatusView Status()
    {
        lock (gate)
        {
            return new ClusterStatusView(nodes.Select(n => new NodeStatus(n.Id, n.Available ? "AVAILABLE" : "FAILED", n.Leader ? "LEADER" : "FOLLOWER", n.Responding, n.VoteYes, n.Accepted, n.Peers.ToArray())).ToArray(), last, accepted);
        }
    }

    public void SetAvailable(int id, bool available)
    {
        lock (gate)
        {
            Node(id).Responding = available;
            Check();
        }
    }

    public void SetVote(int id, bool yes)
    {
        lock (gate) { Node(id).VoteYes = yes; }
    }

    public void Check()
    {
        lock (gate)
        {
            bool changed = false;
            int sender = nodes.FirstOrDefault(n => n.Responding)?.Id ?? 1;
            foreach (ConsensusNode node in nodes)
            {
                bool available = Send(new NodeMessage(sender, node.Id, "HEARTBEAT", Guid.Empty))?.Type == "ALIVE";
                if (node.Available != available)
                {
                    node.Available = available;
                    if (available && accepted != null)
                    {
                        Send(new NodeMessage(sender, node.Id, "COMMIT", Guid.NewGuid(), accepted));
                    }
                    Log($"Node {node.Id} {(available ? "AVAILABLE" : "FAILED")}");
                    changed = true;
                }
            }
            if (changed) { Elect(); }
        }
    }

    public void Elect()
    {
        lock (gate)
        {
            Log("Election started");
            foreach (ConsensusNode node in nodes)
            {
                node.Leader = false;
                Log($"Node {node.Id} {(node.Available ? "AVAILABLE" : "FAILED")} FOLLOWER");
            }
            ConsensusNode? leader = nodes.Where(n => n.Available).OrderByDescending(n => n.Id).FirstOrDefault();
            if (leader != null) { leader.Leader = true; Log($"Node {leader.Id} elected LEADER"); }
            else { Log("No leader available"); }
        }
    }

    public (VoteResult Vote, OperationResult? Result) Execute(string proposal, Func<OperationResult> action)
    {
        if (string.IsNullOrWhiteSpace(proposal)) { throw new ArgumentException("Пустое предложение"); }
        lock (gate)
        {
            Check();
            ConsensusNode? leader = nodes.SingleOrDefault(n => n.Leader && n.Available);
            int yes = 0, no = 0;
            Guid proposalId = Guid.NewGuid();
            Log($"Leader Node {leader?.Id} PROPOSE {proposal} ({proposalId})");
            if (leader != null)
            {
                foreach (ConsensusNode node in nodes.Where(n => n.Available))
                {
                    NodeMessage? response = Send(new NodeMessage(leader.Id, node.Id, "PROPOSE", proposalId, proposal));
                    if (response?.Type == "YES") { yes++; }
                    else if (response?.Type == "NO") { no++; }
                }
            }
            last = new VoteResult(proposal, leader?.Id, yes, no, 3, yes >= 3);
            Log($"YES={yes} NO={no} Majority=3 CONSENSUS {(last.Achieved ? "ACHIEVED" : "NOT ACHIEVED")}");
            if (!last.Achieved) { return (last, null); }
            OperationResult result;
            try { result = action(); }
            catch (Exception ex) { Log($"Storage error: {ex.Message}"); throw; }
            if (result.StatusCode < 400)
            {
                accepted = proposal;
                foreach (ConsensusNode node in nodes.Where(n => n.Available))
                {
                    Send(new NodeMessage(leader!.Id, node.Id, "COMMIT", proposalId, proposal));
                }
                Log($"Decision committed: {proposal}");
            }
            else { Log($"Storage rejected proposal: {result.StatusCode}"); }
            return (last, result);
        }
    }
}
