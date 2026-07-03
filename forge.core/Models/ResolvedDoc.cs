using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>ai-fleet-orchestration D-2: a resolved RAG doc after baseline+client merge.</summary>
public record ResolvedDoc(string RelativePath, string FullPath, DocSource Source);
