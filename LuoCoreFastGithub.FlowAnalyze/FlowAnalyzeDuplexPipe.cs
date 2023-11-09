using System.IO.Pipelines;

namespace LuoCoreFastGithub.FlowAnalyze
{
    sealed class FlowAnalyzeDuplexPipe : DelegatingDuplexPipe<FlowAnalyzeStream>
    {
        public FlowAnalyzeDuplexPipe(IDuplexPipe duplexPipe, IFlowAnalyzer flowAnalyzer) :
            base(duplexPipe, stream => new FlowAnalyzeStream(stream, flowAnalyzer))
        {
        }
    }
}
