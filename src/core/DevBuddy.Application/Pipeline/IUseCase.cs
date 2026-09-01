namespace DevBuddy.Application.Pipeline;

/// <summary>
/// Non-generic view of a use case, so the catalogue and the MCP tool surface can enumerate them
/// without knowing their request and response types.
/// </summary>
public interface IUseCase
{
    UseCaseDescriptor Descriptor { get; }
}
