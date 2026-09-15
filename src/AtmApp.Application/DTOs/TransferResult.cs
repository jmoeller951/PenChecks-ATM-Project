using AtmApp.Domain.Entities;

namespace AtmApp.Application.DTOs;

public record TransferResult(Transaction OutgoingTransaction, Transaction IncomingTransaction);
