using Microsoft.SemanticKernel;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IKernelFactory
{
    Kernel CreateKernel();
    bool IsConfigured { get; }
}
