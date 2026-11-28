using Inkitty.Core.Compilation;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Playback;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Playback;

public class ReachabilityCheckerTests
{
    private static async Task<ReachabilityReport> Check(ProjectSnapshot snapshot, int runs = 50)
    {
        var result = new InkCompilerService().Compile(snapshot);
        var story = result is CompileSucceeded ok ? ok.Story : throw new InvalidOperationException(string.Join('\n', result.Issues));
        return await ReachabilityChecker.CheckAsync(story, snapshot, new ReachabilityOptions(runs));
    }

    private static Task<ReachabilityReport> Check(string ink, int runs = 50) => Check(InMemoryProject.OneFile(ink), runs);

    private static int[] UnreachedLines(ReachabilityReport report) => [.. report.Unreached.Select(r => r.Line)];

    [Test]
    public async Task Every_branch_of_a_choice_tree_is_reached()
    {
        var report = await Check("""
            Start.
            * [Left] Went left.
                * * [Deeper] Deep left.
                    -> END
                * * [Back] Turned back.
                    -> END
            * [Right] Went right.
                -> END
            """);

        await Assert.That(report.Unreached).IsEmpty();
        await Assert.That(report.CoverableLines).IsEqualTo(5);
    }

    [Test]
    public async Task Text_after_a_divert_and_knots_never_diverted_to_are_unreached()
    {
        var report = await Check("""
            Hello.
            -> next
            Never shown.
            === next ===
            Arrived.
            -> END
            === orphan ===
            Lonely.
            * [A choice nobody sees] Hidden.
            -> END
            """);

        await Assert.That(UnreachedLines(report)).IsEquivalentTo([3, 8, 9]);
    }

    [Test]
    public async Task Impossible_conditions_are_unreached()
    {
        var report = await Check("""
            VAR met = false
            {met:
                You know each other.
            - else:
                Strangers.
            }
            * {met} [Wave] Waved.
            * [Nod] Nodded.
            - -> END
            """);

        // Line granularity: an inline {met: a|b} would count as reached once either branch prints.
        await Assert.That(UnreachedLines(report)).IsEquivalentTo([3, 7]);
    }

    [Test]
    public async Task Logic_only_lines_and_variable_text_are_not_checked()
    {
        var report = await Check("""
            VAR name = "Unused text in a declaration"
            ~ temp x = "also data"
            Hello.
            -> END
            """);

        await Assert.That(report.CoverableLines).IsEqualTo(1);
        await Assert.That(report.Unreached).IsEmpty();
    }

    [Test]
    public async Task Lines_in_included_files_are_reported_against_that_file()
    {
        var snapshot = InMemoryProject.Snapshot(
            ("main.ink", "INCLUDE other.ink\nStart.\n-> END\n"),
            ("other.ink", "=== unused ===\nNobody comes here.\n-> END\n"));

        var report = await Check(snapshot);

        await Assert.That(report.Unreached.Select(r => r.ToString())).IsEquivalentTo(["other.ink:2"]);
        await Assert.That(report.ToIssues().Single().Severity).IsEqualTo(IssueSeverity.Warning);
        await Assert.That(report.ToIssues().Single().Origin).IsEqualTo(IssueOrigin.Reachability);
    }

    [Test]
    public async Task Endless_stories_still_finish()
    {
        var report = await Check("""
            -> loop
            === loop ===
            Round again.
            * [Again] -> loop
            * [Stop] Stopped.
                -> END
            """, runs: 5);

        await Assert.That(report.Runs).IsLessThanOrEqualTo(5);
        await Assert.That(report.Unreached).IsEmpty();
    }

    [Test]
    public async Task Stops_early_once_everything_is_reached()
    {
        var report = await Check("Just one line.\n-> END\n", runs: 1000);

        await Assert.That(report.Runs).IsEqualTo(1);
    }

    [Test]
    public async Task Runtime_errors_dont_stop_the_check()
    {
        var report = await Check("""
            * [Break] -> broken
            * [Fine] All good.
                -> END
            === broken ===
            ~ temp x = 1 / 0
            Never printed.
            -> END
            """);

        await Assert.That(UnreachedLines(report)).Contains(6);
        await Assert.That(UnreachedLines(report)).DoesNotContain(2);
    }

    [Test]
    public async Task Leaves_the_compiled_story_untouched()
    {
        var snapshot = InMemoryProject.OneFile("Hi.\n* [Go] Gone.\n-> END\n");
        var compiled = ((CompileSucceeded)new InkCompilerService().Compile(snapshot)).Story;

        await ReachabilityChecker.CheckAsync(compiled, snapshot, new ReachabilityOptions(3));

        using var session = new PlaySession(compiled, snapshot);
        var events = session.ContinueUntilChoice(PlaybackBudget.Default);
        await Assert.That(events.OfCase<TextOutput>().Select(t => t.Text)).IsEquivalentTo(["Hi."]);
    }

    [Test]
    public async Task Finds_lines_behind_a_long_run_of_particular_choices()
    {
        // A combination lock: twelve answers in an irregular order, and a wrong one ends the story.
        // Playing from the beginning gets it right 1 time in 4,096; continuing from saved points
        // gets there one answer at a time.
        const string answers = "LRRLRLLLRRLR";
        var ink = new System.Text.StringBuilder("-> step0\n");
        for (var i = 0; i < answers.Length; i++)
        {
            var (right, wrong) = answers[i] == 'L' ? ("Left", "Right") : ("Right", "Left");
            ink.Append($"=== step{i} ===\n* [{right}] -> step{i + 1}\n* [{wrong}] Click. The lock resets.\n    -> END\n");
        }

        ink.Append($"=== step{answers.Length} ===\nThe lock opens.\n-> END\n");

        var report = await Check(ink.ToString(), runs: 300);

        await Assert.That(report.Unreached).IsEmpty();
    }
}
