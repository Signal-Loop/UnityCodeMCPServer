Yout goal is to remove dependency on unitask, without sacrificing performance, experience and reliability.
Create plan how to replace Unitask in the project, by providing your own focused implenetation.
You can simplyfy it to async/await pattern or Unity coroutines, but you need to ensure that it can handle the same scenarios as Unitask, such as cancellation, exception handling. Your main goal i reliability and robustness. If neccessary, refer to Unitask implementation, but add adnotation to source files if it is based on it.
Place the implementation in a separate folder "Assets\Plugins\UnityCodeMcpServer\Editor\AsyncAwait"/
You can modify the existing codebase to use the new implementation, change signatures and API as needed. Breaking changes are allowed. You have fill freedom to create solition that fulfills requirements.
Keep it Simple, Use Kiss, Yagni and responsibility separation. Do not leak implementation details to code using it.

Unitask source: C:\Users\tbory\source\Workspaces\UniTask

Verify the changes with unity tests and E2E checks using 'execute_csharp_scripts...' tool.
 openclaw config set channels.telegram.allowFrom '[8561337291]'
 openclaw config set channels.telegram.dmPolicy "allowlist"

 @Northegar
Id: 8561337291
First: Tomek
Lang: en
Registered: Check Date

🧠 Explanations and answers
Free AI → DeepSeek & ChatGPT

🖼 Visualize your ideas
Make Image → NanoBanana
