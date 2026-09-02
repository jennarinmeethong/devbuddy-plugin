import { GlobalRegistrator } from "@happy-dom/global-registrator";

// A DOM for the smoke test. The alternative is testing the screens through their internals, which
// proves the internals agree with themselves and nothing about whether a person can reach a page.
GlobalRegistrator.register({ url: "http://localhost/" });
