using System;
using GuardaCat.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuardaCat.Server.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031230000_TagsAndPostLikes")]
public partial class TagsAndPostLikes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Tags", x => x.Name));

        migrationBuilder.CreateTable(
            name: "PostLikes",
            columns: table => new
            {
                PostId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostLikes", x => new { x.PostId, x.UserId });
                table.ForeignKey(
                    name: "FK_PostLikes_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PostLikes_Posts_PostId",
                    column: x => x.PostId,
                    principalTable: "Posts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PostTags",
            columns: table => new
            {
                PostId = table.Column<Guid>(type: "uuid", nullable: false),
                TagName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostTags", x => new { x.PostId, x.TagName });
                table.ForeignKey(
                    name: "FK_PostTags_Posts_PostId",
                    column: x => x.PostId,
                    principalTable: "Posts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PostTags_Tags_TagName",
                    column: x => x.TagName,
                    principalTable: "Tags",
                    principalColumn: "Name",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PostLikes_UserId",
            table: "PostLikes",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_PostTags_TagName_PostId",
            table: "PostTags",
            columns: new[] { "TagName", "PostId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PostLikes");
        migrationBuilder.DropTable(name: "PostTags");
        migrationBuilder.DropTable(name: "Tags");
    }
}
