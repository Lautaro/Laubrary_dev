using Laubrary.Loom;

namespace Laubrary.Story
{
    // Runs Screenplays on Loom's shared GraphRunner. All the walking (bookmarks, forks, parking, the reversible
    // patch stack, the traversal trace) lives in the base; StoryRunner only supplies the Story-flavoured context
    // (Journal + the rule bridge / presenter / conditions the game injects) and the Screenplay entry point.
    public class StoryRunner : GraphRunner<Page, StoryContext>
    {
        public IRuleBridge RuleBridge;
        public IMessagePresenter Presenter;
        public IConditionSource Conditions;
        public Journal Journal = new Journal();

        // Start a screenplay (game-agnostic: inject RuleBridge / Presenter / Conditions before running).
        public void Run(Screenplay sp) => RunGraph(sp);

        protected override StoryContext MakeContext(IRunnableGraph<Page> graph) => new StoryContext
        {
            Runner = this,
            Screenplay = graph as Screenplay,
            Journal = Journal,
            Rules = RuleBridge,
            Presenter = Presenter,
            Conditions = Conditions,
        };
    }
}
