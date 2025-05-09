using System.Threading;
using System.Threading.Tasks;

namespace TechChallenge.Calculator.Api.Data;

public interface ICalculatorRepository
{ 
    Task<CalculatedTotalEmission> GetCalculatedValue(
        string userId,
        long from,
        long to,
        CancellationToken cancellationToken);
}