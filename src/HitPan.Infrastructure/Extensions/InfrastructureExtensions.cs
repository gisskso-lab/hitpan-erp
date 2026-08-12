using HitPan.Application.Interfaces;
using HitPan.Infrastructure.Configuration;
using HitPan.Infrastructure.Persistence;
using HitPan.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using System.Data;

namespace HitPan.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // 사고 #46 봉합 (WS-20260612-01 2026-06-12): 환경변수 영역 폐기 + db.conf 직접 영역
        //   사장님 결재 — 싱글 각각 설치 영역 정합 (멀티테넌트 = Phase 3 영역 클라우드 영역)
        //   사고 #41·#42·#39 영역 자동 해결 — 회사별 EXE 영역 자기 폴더 db.conf 영역만 박힘
        //   TenantConfigReader = db.conf → 환경변수 폴백 영역 안전망 박힘
        var host = TenantConfigReader.Get("DB_HOST") ?? "localhost";
        var port = TenantConfigReader.Get("DB_PORT") ?? "3306";
        var db = TenantConfigReader.GetRequired("DB_NAME");
        var user = TenantConfigReader.GetRequired("DB_USER");
        var pwd = TenantConfigReader.GetRequired("DB_PASSWORD");

        // DefaultCommandTimeout=90: 저장 후 partner_balance 집계 뷰가 대량 집계를 할 때
        // 기본 30초로는 부족해 Command Timeout → 롤백 → 유실이 발생. 90초 안전마진.
        // 2026-05-13 야간 #3: AllowLoadLocalInfile=true — MySqlBulkCopy(LOAD DATA LOCAL INFILE) 활성화.
        // 마이그 성능 8분→30초 목표. 일반 쿼리에는 영향 없음.
        // 🔴 AllowUserVariables=true (봉합 2026-08-12, 실측 적발)
        //   MigrationRunner 가 이 연결로 DB-NN 파일을 실행하는데, 멱등 마이그는
        //   'SET @col_exists := (SELECT ...)' + PREPARE/EXECUTE 관용구를 쓴다.
        //   이 옵션이 없으면 MySqlConnector 가 '@col_exists' 를 **파라미터로 착각**해
        //     "Parameter '@col_exists' must be defined." 로 마이그가 통째로 중단된다.
        //   실측: API 기동 로그 — "🛑 DB 스키마 갱신 실패 — 실패 마이그: DB-88".
        //   ⇒ DB-88·DB-89·DB-90 이 전부 이 문법이라 **고객 PC 에서 적용되지 않고 있었다.**
        //     (특히 DB-89 는 메인PC 잠김 봉합이다 — 1.2.67 로 게시했으나 실제로는 안 돌았다.)
        //   개발 PC 에서 못 본 이유: 마이그를 mysql.exe 로 직접 넣어 확인했기 때문이다.
        //   그 경로는 이 옵션과 무관하다(사장님 원칙 — 개발PC 정상은 검증이 아니다).
        // 🔴 GuidFormat=None (봉합 2026-08-12, 사장님 실측 적발 — 양식템플릿 500)
        //   MySqlConnector 는 기본으로 **CHAR(36) 컬럼을 Guid 로 돌려준다.**
        //   우리 DTO 는 이런 ID 를 전부 string 으로 받으므로 Dapper 가 값을 못 넣고 터진다:
        //     System.Data.DataException: Error parsing column 0 (TemplateId=... - Guid)
        //   ⇒ 화면은 "양식 목록 조회 실패 … 500" 만 보인다. 원인이 화면에 안 드러난다.
        //
        //   ⚠️ 왜 지금까지 안 터졌나 — 대부분의 표가 varchar(36) 이라서다.
        //     partners.partner_id = varchar(36) → String 으로 와서 멀쩡했다.
        //     form_templates.template_id = char(36) → Guid 로 와서 터졌다.
        //     같은 폭탄이 common_codes·item_specs·migration_jobs·sync_tokens 등
        //     **char(36) 을 쓰는 표 전체**에 잠복해 있었다. 화면을 안 열어봤을 뿐이다.
        //
        //   None = "Guid 로 바꾸지 말고 있는 그대로(String) 달라". 우리 코드와 맞는 유일한 값이다.
        //   실측(2026-08-12): 설정 없음/Char36 → 둘 다 FAIL(위 예외 그대로 재현),
        //                     None → 반환형 String, 정상 조회.
        var connStr = $"Server={host};Port={port};Database={db};User={user};Password={pwd};DefaultCommandTimeout=90;AllowLoadLocalInfile=true;AllowUserVariables=true;GuidFormat=None;";
        // v1.0.6: AutoDetect는 기동 시 DB 연결을 선행 호출 → 설치 직후 타이밍 민감 구간에서 지연·크래시 유발.
        // 설치파일은 MariaDB 11.4 MSI를 동봉하므로 고정 버전으로 안정화.
        var serverVersion = new MariaDbServerVersion(new Version(11, 4, 0));

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connStr,
                serverVersion,
                x => x.MigrationsAssembly("HitPan.Infrastructure")));
        services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));

        // 2026-05-14 정공법(축 3, SEC-04): 마이그 전용 connection 풀을 일반 컨트롤러 풀과 물리 분리.
        // ApplicationName/Timeout/AllowLoadLocalInfile/PoolSize가 달라 MySqlConnector가 별도 pool 인스턴스 유지.
        // → SET SESSION fk/unique/innodb_flush가 새도록 만들어도 일반 컨트롤러 connection 0 오염.
        // Singleton: factory는 conn string만 캐싱하고 매 호출 새 connection 발급 → thread-safe.
        services.AddSingleton<IMigrationDbConnectionFactory, MigrationDbConnectionFactory>();

        services.AddScoped<CommonCodeSeeder>();
        services.AddScoped<SystemSeeder>();

        // WS-11 정공법 축 5 (사장님 명령 2026-05-14): POTHER 4 리포지토리 DI 등록.
        services.AddScoped<IPartnerContactRepository, PartnerContactRepository>();
        services.AddScoped<IServiceTicketRepository, ServiceTicketRepository>();
        services.AddScoped<IDeliveryTrackingRepository, DeliveryTrackingRepository>();
        services.AddScoped<ICalendarEventRepository, CalendarEventRepository>();

        return services;
    }
}
