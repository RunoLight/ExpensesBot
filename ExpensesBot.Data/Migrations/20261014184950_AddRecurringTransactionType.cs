using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpensesBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTransactionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "RecurringTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "RecurringTransactions");
        }
    }
}
