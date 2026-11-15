import ts from "typescript";

// This guard scans product source, not test/release orchestration. FFI is restricted to
// two reviewed adapters and literal OS libraries. It is not a sandbox for dependencies.
export function violations(source: string, file: string): string[] {
  const tree = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true);
  const problems: string[] = [];
  const allowedLibraries: Record<string, string[]> = {
    "windows.ts": ["advapi32.dll", "kernel32.dll"],
    "keychain.ts": ["/System/Library/Frameworks/Security.framework/Security", "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"],
  };
  const runtimeModules = new Set(["node:crypto", "node:fs", "node:os", "node:path", "node:readline", "node:stream", "node:url"]);
  function moduleAllowed(specifier: string): boolean {
    return runtimeModules.has(specifier) || /^\.\/[a-z][a-z0-9-]*\.js$/.test(specifier)
      || (specifier === "koffi" && !!allowedLibraries[file]);
  }
  function visit(node: ts.Node): void {
    if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier) {
      if (!ts.isStringLiteral(node.moduleSpecifier) || !moduleAllowed(node.moduleSpecifier.text)) problems.push("unapproved module");
      if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier) && node.moduleSpecifier.text === "koffi"
        && (node.importClause?.name?.text !== "koffi" || node.importClause.namedBindings)) problems.push("aliased FFI import");
    }
    if (ts.isIdentifier(node) && ["eval", "Function", "require", "createRequire", "getBuiltinModule"].includes(node.text)) problems.push("dynamic execution or loading reference");
    if (ts.isElementAccessExpression(node) && node.argumentExpression && ts.isStringLiteral(node.argumentExpression)
      && /^(?:eval|Function|require|dlopen|spawn|spawnSync|exec|execSync|execFile|execFileSync|getBuiltinModule|createRequire|\$)$/.test(node.argumentExpression.text)) problems.push("computed execution reference");
    if (ts.isPropertyAccessExpression(node)) {
      if (/^(?:spawn|spawnSync|exec|execSync|execFile|execFileSync|dlopen|\$|Command)$/.test(node.name.text)) problems.push("execution reference");
      if (node.expression.getText(tree) === "koffi" && node.name.text === "load"
        && (!ts.isCallExpression(node.parent) || node.parent.expression !== node)) problems.push("aliased native loader");
    }
    if (ts.isStringLiteral(node) && /^(?:node:)?(?:child_process|cluster|worker_threads|vm)$/.test(node.text)) problems.push("execution module");
    if (ts.isStringLiteral(node) && node.text === "koffi" && !allowedLibraries[file]) problems.push("FFI outside adapter");
    if ((ts.isCallExpression(node) || ts.isNewExpression(node)) && /^(?:eval|Function)$/.test(node.expression.getText(tree))) problems.push("dynamic execution");
    if (ts.isCallExpression(node)) {
      const expression = node.expression.getText(tree);
      if (/\b(?:spawn|spawnSync|exec|execSync|execFile|execFileSync|dlopen)\b/.test(expression)) problems.push("process or native execution");
      if (node.expression.kind === ts.SyntaxKind.ImportKeyword && (node.arguments.length !== 1 || !ts.isStringLiteral(node.arguments[0]!))) problems.push("nonliteral import");
      if (node.expression.kind === ts.SyntaxKind.ImportKeyword && node.arguments[0] && ts.isStringLiteral(node.arguments[0])
        && (!moduleAllowed(node.arguments[0].text) || node.arguments[0].text === "koffi")) problems.push("unapproved dynamic module");
      if (expression === "koffi.load") {
        const argument = node.arguments[0];
        if (node.arguments.length !== 1 || !argument || !ts.isStringLiteral(argument) || !allowedLibraries[file]?.includes(argument.text)) problems.push("unapproved native library");
      }
      if (expression.startsWith("koffi.") && expression !== "koffi.load" && !allowedLibraries[file]) problems.push("FFI outside adapter");
    }
    ts.forEachChild(node, visit);
  }
  visit(tree);
  return problems;
}
