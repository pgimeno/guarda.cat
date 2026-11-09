using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuardaCat.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommentNotificationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommentId",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CommentId",
                table: "OutboxMessages",
                column: "CommentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OutboxMessages_Comments_CommentId",
                table: "OutboxMessages",
                column: "CommentId",
                principalTable: "Comments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutboxMessages_Comments_CommentId",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CommentId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "CommentId",
                table: "OutboxMessages");
        }
    }
}
