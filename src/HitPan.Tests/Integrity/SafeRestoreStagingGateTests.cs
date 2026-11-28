using System.Reflection;
using System.Text;
using HitPan.API.Controllers;
using HitPan.Application.Services;
using HitPan.Contracts.Idempotency;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2② — 걸음 ③-2 변환기 · 출력 2차 검사 · Z3 비DB 게이트(작업지시서 §4-1 · §5-1 G14 · G20 · G23).
/// DB 없이 돈다(SKIP 0) — 붓기 · 왕복 실물은 <see cref="SafeRestoreGateDbTests"/>(CI db-gate).
/// </summary>
public sealed class SafeRestoreStagingGateTests
{
    private static RestoreStageDecision Decide(string n) => n switch
    {
        "t_backup" or "t_bin" => RestoreStageDecision.Stage,
        "t_keep" => RestoreStageDecision.Skip,
        _ => RestoreStageDecision.Stop,
    };

    // mariadb-dump 11.4 꼴 — 머리 · SET · DROP/CREATE(외래키 줄) · 자료 묶음 · 뷰 자리표 · 뷰 정의 · 트리거(DELIMITER)
    private const string Golden =
        "/*M!999999\\- enable the sandbox mode */ \n"
      + "-- MariaDB dump 10.19-11.4.10-MariaDB, for Linux (x86_64)\n--\n-- Host: localhost\n"
      + "/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;\n"
      + "/*!40101 SET NAMES utf8mb4 */;\n/*!40103 SET TIME_ZONE='+00:00' */;\n"
      + "/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;\n"
      + "/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;\n"
      + "/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;\n\n"
      + "--\n-- Table structure for table `t_backup`\n--\n\n"
      + "DROP TABLE IF EXISTS `t_backup`;\n"
      + "/*!40101 SET @saved_cs_client     = @@character_set_client */;\n/*!40101 SET character_set_client = utf8mb4 */;\n"
      + "CREATE TABLE `t_backup` (\n  `id` int(11) NOT NULL,\n  `tenant_id` varchar(36) NOT NULL,\n"
      + "  `note` varchar(100) DEFAULT 'a;b,c' COMMENT 'x)\\' ENGINE=MyISAM',\n  `amt` decimal(18,2) DEFAULT 0.00,\n"
      + "  PRIMARY KEY (`id`),\n  KEY `ix_t` (`tenant_id`),\n"
      + "  CONSTRAINT `fk_t_backup_keep` FOREIGN KEY (`tenant_id`) REFERENCES `t_keep` (`tenant_id`) ON DELETE CASCADE\n"
      + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;\n"
      + "/*!40101 SET character_set_client = @saved_cs_client */;\n\n"
      + "LOCK TABLES `t_backup` WRITE;\n/*!40000 ALTER TABLE `t_backup` DISABLE KEYS */;\n"
      + "INSERT INTO `t_backup` VALUES (1,'T1','it''s; DROP TABLE x',1.50),(2,'T1',NULL,-3e-2);\n"
      + "/*!40000 ALTER TABLE `t_backup` ENABLE KEYS */;\nUNLOCK TABLES;\n\n"
      + "DROP TABLE IF EXISTS `t_keep`;\n"
      + "CREATE TABLE `t_keep` (\n  `tenant_id` varchar(36) NOT NULL,\n  PRIMARY KEY (`tenant_id`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;\n"
      + "LOCK TABLES `t_keep` WRITE;\nINSERT INTO `t_keep` VALUES ('T1');\nUNLOCK TABLES;\n\n"
      + "--\n-- Temporary table structure for view `v_x`\n--\n\n"
      + "DROP TABLE IF EXISTS `v_x`;\n/*!50001 DROP VIEW IF EXISTS `v_x`*/;\n"
      + "SET @saved_cs_client     = @@character_set_client;\nSET character_set_client = utf8mb4;\n"
      + "/*!50001 CREATE VIEW `v_x` AS SELECT\n 1 AS `id` */;\nSET character_set_client = @saved_cs_client;\n\n"
      + "/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;\n/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES' */ ;\n"
      + "DELIMITER ;;\n/*!50003 CREATE*/ /*!50017 DEFINER=`hitpan`@`%`*/ /*!50003 TRIGGER trg_x BEFORE INSERT ON `t_backup` FOR EACH ROW BEGIN\n"
      + "  SET NEW.amt = 1; DELETE FROM t_keep;\nEND */;;\nDELIMITER ;\n/*!50003 SET sql_mode              = @saved_sql_mode */ ;\n\n"
      + "--\n-- Final view structure for view `v_x`\n--\n\n/*!50001 DROP VIEW IF EXISTS `v_x`*/;\n"
      + "/*!50001 SET @saved_cs_client          = @@character_set_client */;\n"
      + "/*!50001 CREATE ALGORITHM=UNDEFINED */\n/*!50013 DEFINER=`hitpan`@`%` SQL SECURITY DEFINER */\n/*!50001 VIEW `v_x` AS select 1 AS `id` */;\n"
      + "/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;\n-- Dump completed on 2026-11-28 12:00:00\n";

    private static List<RestoreDumpStatement> Run(string dump) => Run(Encoding.UTF8.GetBytes(dump));

    private static List<RestoreDumpStatement> Run(byte[] dump)
    {
        using var ms = new MemoryStream(dump);
        var list = RestoreDumpConverter.Convert(ms, Decide).ToList();
        foreach (var s in list) RestoreStageOutputCheck.Verify(s.Sql);
        return list;
    }

    private static string Text(RestoreDumpStatement s) => Encoding.UTF8.GetString(s.Sql);

    [Fact(DisplayName = "G14-S1 🔴 정상 덤프 꼴(golden) 통과 — 백업 값 표만 rs_new__ · 외래키 줄 · 앞 줄 쉼표 뺌 · 지금 값 · 뷰 · 트리거 0")]
    public void G14_S1_정상덤프_통과()
    {
        var outp = Run(Golden);
        Assert.Equal(3, outp.Count);
        Assert.Equal("DROP TABLE IF EXISTS `rs_new__t_backup`", Text(outp[0]));
        var create = Text(outp[1]);
        Assert.StartsWith("CREATE TABLE `rs_new__t_backup` (\n", create, StringComparison.Ordinal);
        Assert.DoesNotContain("FOREIGN KEY", create, StringComparison.Ordinal);
        Assert.Contains("KEY `ix_t` (`tenant_id`)\n) ENGINE=InnoDB", create, StringComparison.Ordinal);   // 앞 줄 끝 쉼표 뺌
        Assert.Contains("COMMENT 'x)\\' ENGINE=MyISAM'", create, StringComparison.Ordinal);               // 따옴표 안 손 0
        Assert.Equal("INSERT INTO `rs_new__t_backup` VALUES (1,'T1','it''s; DROP TABLE x',1.50),(2,'T1',NULL,-3e-2)", Text(outp[2]));
        Assert.All(outp, s => Assert.Equal("t_backup", s.SourceTable));
    }

    [Fact(DisplayName = "G14-S1b 🔴 (고침 1) mariadb-dump 11.x 실물 묶음 INSERT 꼴 「 VALUES\\n(…),\\n(…)」 · 칸 목록 꼴 통과 — 바이트 그대로 rs_new__")]
    public void G14_S1b_실물_VALUES_줄바꿈_통과()
    {
        // 11.4.10 · 11.7.2 실행 파일 글자 틀 " VALUES\n" (CI db-gate 11.8.6 · 런 46109988942 G3 stage_failed 의 원인 꼴)
        var dump = Golden
            + "DROP TABLE IF EXISTS `t_bin`;\nCREATE TABLE `t_bin` (\n  `b` varbinary(255) DEFAULT NULL,\n  `s` varchar(30) DEFAULT NULL\n) ENGINE=InnoDB;\n"
            + "LOCK TABLES `t_bin` WRITE;\n/*!40000 ALTER TABLE `t_bin` DISABLE KEYS */;\n"
            + "INSERT INTO `t_bin` VALUES\n(0x00FF,'a\\'b;'),\n(NULL,'T2');\n"
            + "INSERT INTO `t_bin` (`b`, `s`) VALUES (0x01,'c'),\n(NULL,NULL);\n"
            + "/*!40000 ALTER TABLE `t_bin` ENABLE KEYS */;\nUNLOCK TABLES;\n";
        var outp = Run(dump);
        Assert.Equal(7, outp.Count);
        Assert.Equal("INSERT INTO `rs_new__t_bin` VALUES\n(0x00FF,'a\\'b;'),\n(NULL,'T2')", Text(outp[5]));
        Assert.Equal("INSERT INTO `rs_new__t_bin` (`b`, `s`) VALUES (0x01,'c'),\n(NULL,NULL)", Text(outp[6]));
        Assert.All(outp.Skip(3), s => Assert.Equal("t_bin", s.SourceTable));
    }

    public static IEnumerable<object[]> Variants() => new[]
    {
        new object[] { "VALUES 뒤 탭(정확한 꼴 둘 밖)", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES\t(9,'T1',NULL,0);\n" },
        new object[] { "VALUES 줄바꿈 꼴 속 둘째 문장", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES\n(9,'T1',NULL,0);DELETE FROM `t_keep`;\n" },
        new object[] { "GRANT", "GRANT ALL ON *.* TO 'x'@'%';\n" },
        new object[] { "DROP DATABASE", "DROP DATABASE `hitpan_erp`;\n" },
        new object[] { "둘째 문장 숨긴 INSERT", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES (9,'T1',NULL,0);DELETE FROM `t_keep`;\n" },
        new object[] { "따옴표 없는 이름", "DROP TABLE IF EXISTS t_backup2;\n" },
        new object[] { "소문자", "insert into `t_backup` values (9,'T1',NULL,0);\n" },
        new object[] { "이름 사이 주석", "INSERT INTO /*x*/ `t_backup` VALUES (9,'T1',NULL,0);\n" },
        new object[] { "줄바꿈", "INSERT INTO\n`t_backup` VALUES (9,'T1',NULL,0);\n" },
        new object[] { "자료 묶음 밖 ALTER", "/*!40000 ALTER TABLE `t_keep` DISABLE KEYS */;\n" },
        new object[] { "IF EXISTS 없는 뷰 DROP", "/*!50001 DROP VIEW `v_live`*/;\n" },
        new object[] { "점 붙은 이름", "INSERT INTO `other_db`.`t_backup` VALUES (9,'T1',NULL,0);\n" },
        new object[] { "짝 없는 DROP", "DROP TABLE IF EXISTS `t_keep`;\n" },
        new object[] { "하위 질의", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES ((SELECT 1),'T1',NULL,0);\n" },
        new object[] { "변수", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES (@a,'T1',NULL,0);\n" },
        new object[] { "분류 안 된 표", "CREATE TABLE `t_unknown` (\n  `id` int NOT NULL\n) ENGINE=InnoDB;\n" },
        new object[] { "InnoDB 아님", "CREATE TABLE `t_bin` (\n  `id` int NOT NULL\n) ENGINE=MyISAM;\n" },
        new object[] { "DATA DIRECTORY", "CREATE TABLE `t_bin` (\n  `id` int NOT NULL\n) ENGINE=InnoDB DATA DIRECTORY='/tmp';\n" },
        new object[] { "전역 SET", "/*!40101 SET GLOBAL local_infile=1 */;\n" },
        new object[] { "USE", "USE `other_db`;\n" },
        new object[] { "닫히지 않은 따옴표", "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES (9,'T1;\n" },
    };

    [Theory(DisplayName = "G14-S2 🔴 허용 목록 밖 · 꼴 어긋난 덤프 = 붓기 전에 멈춤(변이 덤프)")]
    [MemberData(nameof(Variants))]
    public void G14_S2_변이덤프_멈춤(string label, string tail)
    {
        var ex = Assert.Throws<RestoreDumpRejectedException>(() => Run(Golden + tail));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message), label);
    }

    // 🔴 고침 2 — mariadb-dump 11.8.6 원본(client/mysqldump.cc · CI db-gate 덤프 도구 판) 출력문을 그대로 옮긴 꼴(개발명세서 §9 표).
    //   머리 write_header(:788 ~ :831) · 표 get_table_structure(:3315 ~ :3474) · 자료 dump_table(:4489 ~ :4793 · 묶음 = " VALUES\n" + ",\n")
    //   · 트리거 dump_trigger(:3957 ~ :3989 · switch_cs_variables · switch_sql_mode · cover_definer_clause) · 뷰 자리표(:3315 ~ :3430) · 뷰 정의(get_view_structure) · 꼬리 write_footer(:846 ~ :879)
    private const string Golden118 =
        "/*M!999999\\- enable the sandbox mode */ \n"
      + "-- MariaDB dump 10.19-11.8.6-MariaDB, for debian-linux-gnu (x86_64)\n--\n-- Host: 127.0.0.1    Database: srgate\n"
      + "-- ------------------------------------------------------\n-- Server version\t11.4.10-MariaDB-ubu2404\n"
      + "\n/*M!100101 SET LOCAL SQL_LOG_OFF=0, LOCAL LOG_SLOW_QUERY=0 */;"
      + "\n/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;"
      + "\n/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;"
      + "\n/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;"
      + "\n/*!40101 SET NAMES utf8mb4 */;\n"
      + "/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;\n/*!40103 SET TIME_ZONE='+00:00' */;\n"
      + "/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;\n"
      + "/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;\n"
      + "/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;\n"
      + "/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;\n"
      + "\n--\n-- Table structure for table `t_backup`\n--\n\n"
      + "DROP TABLE IF EXISTS `t_backup`;\n"
      + "/*!40101 SET @saved_cs_client     = @@character_set_client */;\n/*!40101 SET character_set_client = utf8mb4 */;\n"
      + "CREATE TABLE `t_backup` (\n  `id` int(11) NOT NULL AUTO_INCREMENT,\n  `tenant_id` varchar(36) NOT NULL COMMENT '회사 (\"DB-84\")',\n"
      + "  `amt` decimal(15,2) NOT NULL DEFAULT 0.00,\n  `paid` decimal(15,2) NOT NULL DEFAULT 0.00,\n"
      + "  `bal` decimal(15,2) GENERATED ALWAYS AS (`amt` - `paid`) STORED,\n"
      + "  `j` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_bin DEFAULT NULL CHECK (json_valid(`j`)),\n"
      + "  `at` datetime(6) NOT NULL DEFAULT current_timestamp(6) ON UPDATE current_timestamp(6),\n"
      + "  PRIMARY KEY (`id`),\n  KEY `ix_t` (`tenant_id`),\n"
      + "  CONSTRAINT `fk_t_backup_keep` FOREIGN KEY (`tenant_id`) REFERENCES `t_keep` (`tenant_id`) ON DELETE CASCADE,\n"
      + "  CONSTRAINT `chk_amt` CHECK (`amt` >= 0)\n"
      + ") ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='???? 표';\n"
      + "/*!40101 SET character_set_client = @saved_cs_client */;\n"
      + "\n--\n-- Dumping data for table `t_backup`\n--\n\n"
      + "LOCK TABLES `t_backup` WRITE;\n/*!40000 ALTER TABLE `t_backup` DISABLE KEYS */;\n"
      + "INSERT INTO `t_backup` VALUES\n(1,'T1',1.50,0.00,1.50,'{\\\"k\\\":\\\"a;b\\\"}','2026-11-28 05:49:00.123456'),\n(2,'T1',-3e-2,0.00,-0.03,NULL,'2026-11-28 05:49:00.000000');\n"
      + "/*!40000 ALTER TABLE `t_backup` ENABLE KEYS */;\nUNLOCK TABLES;\n"
      + "/*!50003 SET @saved_cs_client      = @@character_set_client */ ;\n/*!50003 SET @saved_cs_results     = @@character_set_results */ ;\n"
      + "/*!50003 SET @saved_col_connection = @@collation_connection */ ;\n/*!50003 SET character_set_client  = utf8mb3 */ ;\n"
      + "/*!50003 SET character_set_results = utf8mb3 */ ;\n/*!50003 SET collation_connection  = utf8mb3_general_ci */ ;\n"
      + "/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;\n/*!50003 SET sql_mode              = '' */ ;\n"
      + "DELIMITER ;;\n/*!50003 CREATE*/ /*!50017 DEFINER=`root`@`%`*/ /*!50003 TRIGGER trg_x BEFORE INSERT ON t_backup FOR EACH ROW SET NEW.amt = IF(NEW.amt > 0, ROUND((NEW.amt / 2) * 100, 2), NEW.amt) */;;\n"
      + "DELIMITER ;\n/*!50003 SET sql_mode              = @saved_sql_mode */ ;\n"
      + "/*!50003 SET character_set_client  = @saved_cs_client */ ;\n/*!50003 SET character_set_results = @saved_cs_results */ ;\n"
      + "/*!50003 SET collation_connection  = @saved_col_connection */ ;\n"
      + "/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;\n"
      + "/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION' */ ;\n"
      + "DELIMITER ;;\n/*!50003 CREATE*/ /*!50017 DEFINER=`root`@`%`*/ /*!50003 TRIGGER `trg_lock`\nBEFORE UPDATE ON `t_backup`\nFOR EACH ROW\nBEGIN\n"
      + "  IF OLD.`amt` IN (1,2) THEN\n    SIGNAL SQLSTATE '45000'\n      SET MESSAGE_TEXT = '발행완료된 건은 수정할 수 없습니다.';\n  END IF;\nEND */;;\n"
      + "DELIMITER ;\n/*!50003 SET sql_mode              = @saved_sql_mode */ ;\n"
      + "\n--\n-- Table structure for table `t_keep`\n--\n\n"
      + "DROP TABLE IF EXISTS `t_keep`;\n"
      + "/*!40101 SET @saved_cs_client     = @@character_set_client */;\n/*!40101 SET character_set_client = utf8mb4 */;\n"
      + "CREATE TABLE `t_keep` (\n  `tenant_id` varchar(36) NOT NULL,\n  PRIMARY KEY (`tenant_id`)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;\n"
      + "/*!40101 SET character_set_client = @saved_cs_client */;\n"
      + "\n--\n-- Dumping data for table `t_keep`\n--\n\n"
      + "LOCK TABLES `t_keep` WRITE;\n/*!40000 ALTER TABLE `t_keep` DISABLE KEYS */;\n/*!40000 ALTER TABLE `t_keep` ENABLE KEYS */;\nUNLOCK TABLES;\n"
      + "\n--\n-- Temporary table structure for view `v_x`\n--\n\n"
      + "DROP TABLE IF EXISTS `v_x`;\n/*!50001 DROP VIEW IF EXISTS `v_x`*/;\n"
      + "SET @saved_cs_client     = @@character_set_client;\nSET character_set_client = utf8mb4;\n"
      + "/*!50001 CREATE VIEW `v_x` AS SELECT\n 1 AS `a`,\n  1 AS `b` */;\nSET character_set_client = @saved_cs_client;\n"
      + "\n--\n-- Dumping routines for database 'srgate'\n--\n"
      + "\n--\n-- Final view structure for view `v_x`\n--\n\n"
      + "/*!50001 DROP VIEW IF EXISTS `v_x`*/;\n"
      + "/*!50001 SET @saved_cs_client          = @@character_set_client */;\n/*!50001 SET @saved_cs_results         = @@character_set_results */;\n"
      + "/*!50001 SET @saved_col_connection     = @@collation_connection */;\n/*!50001 SET character_set_client      = utf8mb4 */;\n"
      + "/*!50001 SET character_set_results     = utf8mb4 */;\n/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;\n"
      + "/*!50001 CREATE ALGORITHM=UNDEFINED */\n/*!50013 DEFINER=`root`@`%` SQL SECURITY DEFINER */\n"
      + "/*!50001 VIEW `v_x` AS select `t`.`id` AS `a`,count(0) * 100 / 2 AS `b` from `t_backup` `t` where `t`.`tenant_id` <> 'a;b' group by `t`.`id` */;\n"
      + "/*!50001 SET character_set_client      = @saved_cs_client */;\n/*!50001 SET character_set_results     = @saved_cs_results */;\n"
      + "/*!50001 SET collation_connection      = @saved_col_connection */;\n"
      + "/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;\n"
      + "\n/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;\n"
      + "/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;\n/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;\n"
      + "/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;\n/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;\n"
      + "/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;\n/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;\n"
      + "\n-- Dump completed on 2026-11-28  5:49:00\n";

    [Fact(DisplayName = "G14-S1c 🔴 (고침 2) mariadb-dump 11.8.6 원본 출력문 꼴 전부(머리 · 표 · 생성 칸 · 자료 · 빈 표 · 트리거 2 · 뷰 자리표 · 뷰 정의 · 꼬리) 통과")]
    public void G14_S1c_11_8_6_원본꼴_통과()
    {
        var outp = Run(Golden118);
        Assert.Equal(3, outp.Count);
        Assert.Equal("DROP TABLE IF EXISTS `rs_new__t_backup`", Text(outp[0]));
        var create = Text(outp[1]);
        Assert.DoesNotContain("FOREIGN KEY", create, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT `chk_amt` CHECK (`amt` >= 0)\n) ENGINE=InnoDB AUTO_INCREMENT=3", create, StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO `rs_new__t_backup` VALUES\n(1,'T1',1.50,", Text(outp[2]), StringComparison.Ordinal);
        Assert.All(outp, s => Assert.Equal("t_backup", s.SourceTable));
    }

    [Fact(DisplayName = "G14-S2b 🔴 (고침 2 · R1) 줄바꿈 꼴 「 VALUES\\n」 속 하위 질의 = 상수 검사가 멈춤(줄바꿈 꼴만 상수 검사 건너뜀 변이 지킴)")]
    public void G14_S2b_줄바꿈꼴_하위질의_상수검사()
    {
        var ex = Assert.Throws<RestoreDumpRejectedException>(() =>
            Run(Golden + "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES\n((SELECT 1),'T1',NULL,0);\n"));
        Assert.Contains("상수 밖", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G14-D (고침 2) 시험 진단 — 멈춘 단계 · 사유 · 멈춘 문장 머리를 글로(DB 게이트 실패 글 도우미가 늘 「재현 0」 아님)")]
    public void G14_D_시험진단_멈춘자리()
    {
        var ok = RestoreDumpDiagnostics.Describe(Encoding.UTF8.GetBytes(Golden118), Decide);
        Assert.StartsWith("[진단] 멈춤 재현 0", ok, StringComparison.Ordinal);

        var bad = RestoreDumpDiagnostics.Describe(Encoding.UTF8.GetBytes(Golden118 + "\n--\n-- x\n--\n\nGRANT ALL ON *.* TO 'x'@'%';\n"), Decide);
        Assert.Contains("단계=1차 변환기", bad, StringComparison.Ordinal);
        Assert.Contains("허용 목록 밖", bad, StringComparison.Ordinal);
        Assert.Contains("그 문장 머리 80=「GRANT ALL ON *.* TO 'x'@'%'", bad, StringComparison.Ordinal);

        var second = RestoreDumpDiagnostics.Describe(Encoding.UTF8.GetBytes(Golden + "LOCK TABLES `t_backup` WRITE;\nINSERT INTO `t_backup` VALUES (9,'T1',NULL,0x0G);\n"), Decide);
        Assert.Contains("INSERT INTO `t_backup` VALUES (9,'T1',NULL,0x0G)", second, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "G20-S 🔴 바이트 단위 — 16진 256 바이트 · 따옴표 안 비ASCII · 이스케이프 바이트가 출력에 그대로")]
    public void G20_S_바이트그대로()
    {
        var hex = "0x" + string.Concat(Enumerable.Range(0, 256).Select(i => i.ToString("X2")));
        var head = Encoding.UTF8.GetBytes(Golden + "DROP TABLE IF EXISTS `t_bin`;\nCREATE TABLE `t_bin` (\n  `b` varbinary(300) DEFAULT NULL,\n  `s` varchar(300) DEFAULT NULL\n) ENGINE=InnoDB;\n"
                                          + "LOCK TABLES `t_bin` WRITE;\nINSERT INTO `t_bin` VALUES (" + hex + ",'");
        var inner = new List<byte>();
        inner.AddRange(Encoding.UTF8.GetBytes("주민 서명 \\0\\'\\\\\\n"));
        for (var c = 0x80; c <= 0xFF; c++) inner.Add((byte)c);   // 따옴표 안 바이트는 글자로 풀지 않음
        var tail = Encoding.UTF8.GetBytes("');\nUNLOCK TABLES;\n");
        var dump = head.Concat(inner).Concat(tail).ToArray();

        using var ms = new MemoryStream(dump);
        var outp = RestoreDumpConverter.Convert(ms, Decide).ToList();
        var ins = outp.Last();
        Assert.Equal("t_bin", ins.SourceTable);
        var expected = Encoding.UTF8.GetBytes("INSERT INTO `rs_new__t_bin` VALUES (" + hex + ",'").Concat(inner).Concat(Encoding.UTF8.GetBytes("')")).ToArray();
        Assert.Equal(expected, ins.Sql);
    }

    [Theory(DisplayName = "G14-S3 🔴 출력 2차 검사(독립) — 임시 표 아님 · 점 붙은 이름 · 따옴표 밖 \\ · ; · @ = 멈춤")]
    [InlineData("INSERT INTO `t_live` VALUES (1)")]
    [InlineData("INSERT INTO `rs_new__t`.`x` VALUES (1)")]
    [InlineData("INSERT INTO `rs_new__t` VALUES (1);DROP TABLE `t_live`")]
    [InlineData("INSERT INTO `rs_new__t` VALUES (\\0)")]
    [InlineData("INSERT INTO `rs_new__t` VALUES (@x)")]
    [InlineData("CREATE TABLE `rs_new__a.b` (\n  `id` int\n) ENGINE=InnoDB")]
    [InlineData("DROP TABLE IF EXISTS `rs_new__t`, `t_live`")]
    [InlineData("DROP DATABASE `rs_new__x`")]
    public void G14_S3_출력2차검사(string sql)
    {
        Assert.Throws<RestoreDumpRejectedException>(() => RestoreStageOutputCheck.Verify(Encoding.UTF8.GetBytes(sql)));
    }

    [Fact(DisplayName = "G14-S4 출력 2차 검사 — 정상 3꼴은 통과(늘 멈추는 검사 = 초록 구멍 막음)")]
    public void G14_S4_출력2차검사_정상통과()
    {
        RestoreStageOutputCheck.Verify(Encoding.UTF8.GetBytes("DROP TABLE IF EXISTS `rs_new__t`"));
        RestoreStageOutputCheck.Verify(Encoding.UTF8.GetBytes("CREATE TABLE `rs_new__t` (\n  `a` decimal(18,2) DEFAULT 0.00\n) ENGINE=InnoDB"));
        RestoreStageOutputCheck.Verify(Encoding.UTF8.GetBytes("INSERT INTO `rs_new__t` VALUES (1,'a.b;\\'@?',0x00FF)"));
    }

    [Fact(DisplayName = "G23-① 🔴 안전 복원 끝점에 [IdempotencyKey] 0 (Z3 · 메서드 · 클래스)")]
    public void G23_1_안전복원_멱등키_없음()
    {
        var m = typeof(BackupController).GetMethod(nameof(BackupController.SafeRestore))
                ?? throw new InvalidOperationException("SafeRestore 끝점 없음");
        Assert.Empty(m.GetCustomAttributes<IdempotencyKeyAttribute>(true));
        Assert.Empty(typeof(BackupController).GetCustomAttributes<IdempotencyKeyAttribute>(true));
        Assert.NotNull(m.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact(DisplayName = "G23-② 🔴 비번 칸 요청 × [IdempotencyKey] 끝점 겹침 = 0 (숫자 출력)")]
    public void G23_2_비번칸_멱등키_겹침()
    {
        var overlap = typeof(BackupController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<IdempotencyKeyAttribute>(true).Any()
                         || t.GetCustomAttributes<IdempotencyKeyAttribute>(true).Any())
                .Where(m => m.GetParameters().Any(p => p.ParameterType.GetProperties()
                    .Any(pp => pp.Name.Contains("Password", StringComparison.OrdinalIgnoreCase))))
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();
        Console.Out.WriteLine($"[G23-②] 비번 칸 × 멱등키 겹침 = {overlap.Count}");
        Assert.True(overlap.Count == 0, "비번 칸 요청에 [IdempotencyKey]: " + string.Join(", ", overlap));
    }
}
