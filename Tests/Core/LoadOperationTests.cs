using System;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class LoadOperationTests
    {
        [Test]
        public void Manual_operation_reports_progress_result_and_errors()
        {
            var operation = new ManualLoadOperation<int>();
            operation.SetProgress(0.3f);

            Assert.That(operation.IsDone, Is.False);
            Assert.That(operation.Progress, Is.EqualTo(0.3f));
            Assert.Throws<InvalidOperationException>(() => _ = operation.Result);

            operation.Complete(5);
            Assert.That(operation.Result, Is.EqualTo(5));
            Assert.That(operation.Progress, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => operation.Fail(new Exception()));

            var failed = LoadOperation.Failed<int>(new FormatException("bad"));
            Assert.That(failed.IsDone, Is.True);
            Assert.That(failed.Error, Is.InstanceOf<FormatException>());
            Assert.That(Assert.Throws<InvalidOperationException>(() => _ = failed.Result).InnerException, Is.SameAs(failed.Error));
        }

        [Test]
        public void Deferred_work_runs_once_when_polled_and_keeps_its_exception()
        {
            var runs = 0;
            var operation = LoadOperation.Of(() => ++runs);

            Assert.That(runs, Is.EqualTo(0));
            Assert.That(operation.IsDone, Is.True);
            Assert.That(operation.Result, Is.EqualTo(1));
            Assert.That(runs, Is.EqualTo(1));

            var broken = LoadOperation.Of<int>(() => throw new InvalidOperationException("no"));
            Assert.That(broken.Error.Message, Is.EqualTo("no"));
        }

        [Test]
        public void Then_starts_the_next_step_with_the_previous_result()
        {
            var first = new ManualLoadOperation<string>();
            var second = new ManualLoadOperation<int>();
            string received = null;
            var chain = first.Then(text => { received = text; return second; }, firstShare: 0.25f);

            first.SetProgress(0.8f);
            Assert.That(chain.Progress, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(received, Is.Null);

            first.Complete("scene");
            second.SetProgress(0.5f);
            Assert.That(chain.IsDone, Is.False);
            Assert.That(received, Is.EqualTo("scene"));
            Assert.That(chain.Progress, Is.EqualTo(0.625f).Within(1e-6));

            second.Complete(7);
            Assert.That(chain.IsDone, Is.True);
            Assert.That(chain.Result, Is.EqualTo(7));

            chain.Dispose();
            Assert.That(first.IsDisposed && second.IsDisposed, Is.True);
        }

        [Test]
        public void Then_stops_on_the_first_error()
        {
            var first = new ManualLoadOperation<int>();
            var started = false;
            var chain = first.Then(_ => { started = true; return LoadOperation.Completed(1); });

            first.Fail(new TimeoutException());

            Assert.That(chain.IsDone, Is.True);
            Assert.That(chain.Error, Is.InstanceOf<TimeoutException>());
            Assert.That(started, Is.False);
        }

        [Test]
        public void Select_turns_a_result_into_another_value()
        {
            var asset = new ManualLoadOperation<int>();
            var definition = asset.Select(value => $"#{value}");

            asset.Complete(4);

            Assert.That(definition.Result, Is.EqualTo("#4"));
        }

        [Test]
        public void All_weights_progress_and_fails_on_the_first_error()
        {
            var scene = new ManualLoadOperation<int>();
            var assets = new ManualLoadOperation<int>();
            var all = LoadOperation.All((scene, 3f), (assets, 1f));

            scene.SetProgress(1);
            Assert.That(all.Progress, Is.EqualTo(0.75f).Within(1e-6));
            Assert.That(all.IsDone, Is.False);

            assets.Fail(new Exception("missing"));
            Assert.That(all.IsDone, Is.True);
            Assert.That(all.Error.Message, Is.EqualTo("missing"));

            all.Dispose();
            Assert.That(scene.IsDisposed && assets.IsDisposed, Is.True);
        }
    }
}
