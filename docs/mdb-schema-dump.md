# 레거시 히트판 MDB 스키마 덤프 (2026-04-29)

출처: `C:\Users\소순근\Desktop\새 폴더\`

이 문서는 매핑 설계의 단일 진실 소스(Single Source of Truth)이다.
새 히트판 ERP의 어떤 테이블·컬럼으로 이관할지 결정하는 근거.

---
## PANDATA.mdb

**테이블 수: 18**

### 테이블 인덱스

| # | 테이블명 | 행 수 |
|---|---|---:|
| 1 | BANKF | 0 |
| 2 | DOCCD | 0 |
| 3 | DOCCD1 | 0 |
| 4 | DOCF1 | 0 |
| 5 | DOCF2 | 0 |
| 6 | DOCF4 | 0 |
| 7 | DOCF5 | 0 |
| 8 | DOCF6 | 0 |
| 9 | DOCF7 | 0 |
| 10 | DOCF9 | 0 |
| 11 | DOCFA | 0 |
| 12 | DOCFB | 0 |
| 13 | DOCFC | 0 |
| 14 | DOCFE | 0 |
| 15 | DOCFO | 0 |
| 16 | DOCFQ | 0 |
| 17 | DOCLT | 0 |
| 18 | REMARK1 | 0 |

### BANKF  (0 rows, 12 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | BK_NO | Text(Wide) | 30 | Y |
| 2 | BK_YMD | Text(Wide) | 8 | Y |
| 3 | BK_JWASU | SmallInt |  | Y |
| 4 | BK_JEN | Text(Wide) | 1 | Y |
| 5 | BK_JEK | Text(Wide) | 30 | Y |
| 6 | BK_AMT | Currency |  | Y |
| 7 | BK_SBUY | Long |  | Y |
| 8 | BK_SYMD | Text(Wide) | 8 | Y |
| 9 | BK_SGU | Text(Wide) | 1 | Y |
| 10 | BK_SSUN | SmallInt |  | Y |
| 11 | BK_cheri | Text(Wide) | 6 | Y |
| 12 | BK_CLA | Text(Wide) | 20 | Y |

### DOCCD  (0 rows, 17 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | CD_CLA | Text(Wide) | 20 | Y |
| 2 | CD_CDNO | Text(Wide) | 20 | Y |
| 3 | CD_SNO | Text(Wide) | 10 | Y |
| 4 | CD_DT | Text(Wide) | 8 | Y |
| 5 | CD_NAME | Text(Wide) | 20 | Y |
| 6 | CD_KIDT | Text(Wide) | 6 | Y |
| 7 | CD_MAMT | Currency |  | Y |
| 8 | CD_MREM | Text(Wide) | 60 | Y |
| 9 | CD_HAL | Currency |  | Y |
| 10 | CD_JEBSUIL | Text(Wide) | 8 | Y |
| 11 | CD_BANK | Text(Wide) | 20 | Y |
| 12 | CD_KDT | Text(Wide) | 8 | Y |
| 13 | CD_KAMT | Currency |  | Y |
| 14 | CD_KNO | Text(Wide) | 30 | Y |
| 15 | CD_GU | Text(Wide) | 1 | Y |
| 16 | CD_REM | Text(Wide) | 20 | Y |
| 17 | CD_REM1 | SmallInt |  | Y |

### DOCCD1  (0 rows, 12 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | CD1_NO | Text(Wide) | 30 | Y |
| 2 | CD1_YMD | Text(Wide) | 8 | Y |
| 3 | CD1_JWASU | SmallInt |  | Y |
| 4 | CD1_JEN | Text(Wide) | 1 | Y |
| 5 | CD1_JEK | Text(Wide) | 50 | Y |
| 6 | CD1_AMT | Currency |  | Y |
| 7 | CD1_SBUY | Long |  | Y |
| 8 | CD1_SYMD | Text(Wide) | 8 | Y |
| 9 | CD1_SGU | Text(Wide) | 1 | Y |
| 10 | CD1_SSUN | SmallInt |  | Y |
| 11 | CD1_cheri | Text(Wide) | 6 | Y |
| 12 | CD1_CLA | Text(Wide) | 20 | Y |

### DOCF1  (0 rows, 14 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | KA_NO | Text(Wide) | 10 | Y |
| 2 | KA_NO1 | SmallInt |  | Y |
| 3 | KA_NO2 | SmallInt |  | Y |
| 4 | KA_PUM | Text(Wide) | 40 | Y |
| 5 | KA_KU | Text(Wide) | 40 | Y |
| 6 | KA_DANW | Text(Wide) | 4 | Y |
| 7 | KA_SU | Currency |  | Y |
| 8 | KA_DAN | Currency |  | Y |
| 9 | KA_KUM | Currency |  | Y |
| 10 | KA_VAT | Currency |  | Y |
| 11 | KA_NAB | Text(Wide) | 8 | Y |
| 12 | KA_REM | Text(Wide) | 30 | Y |
| 13 | KA_DT | Text(Wide) | 8 | Y |
| 14 | KA_DC | Text(Wide) | 15 | Y |

### DOCF2  (0 rows, 16 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | K2_NO | Text(Wide) | 10 | Y |
| 2 | K2_BUY | Text(Wide) | 50 | Y |
| 3 | K2_BUYC | Long |  | Y |
| 4 | K2_SAWON | Text(Wide) | 20 | Y |
| 5 | K2_AMT | Currency |  | Y |
| 6 | K2_VAT | Currency |  | Y |
| 7 | K2_DT | Text(Wide) | 8 | Y |
| 8 | K2_KIDT | Text(Wide) | 20 | Y |
| 9 | K2_KSM | Text(Wide) | 30 | Y |
| 10 | K2_NPJS | Text(Wide) | 30 | Y |
| 11 | K2_JKJK | Text(Wide) | 30 | Y |
| 12 | K2_KITA | Text(Wide) | 120 | Y |
| 13 | K2_LINE | SmallInt |  | Y |
| 14 | K2_GUBUN | Text(Wide) | 2 | Y |
| 15 | K2_HALY | Currency |  | Y |
| 16 | K2_HALK | Currency |  | Y |

### DOCF4  (0 rows, 35 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | TX_IO | Text(Wide) | 1 | Y |
| 2 | TX_NO | Text(Wide) | 8 | Y |
| 3 | TX_PDT | Text(Wide) | 8 | Y |
| 4 | TX_BUY | Long |  | Y |
| 5 | TX_PUM1 | Text(Wide) | 60 | Y |
| 6 | TX_SU1 | Currency |  | Y |
| 7 | TX_DAN1 | Currency |  | Y |
| 8 | TX_KUM1 | Currency |  | Y |
| 9 | TX_VAT1 | Currency |  | Y |
| 10 | TX_PUM2 | Text(Wide) | 60 | Y |
| 11 | TX_SU2 | Currency |  | Y |
| 12 | TX_DAN2 | Currency |  | Y |
| 13 | TX_KUM2 | Currency |  | Y |
| 14 | TX_VAT2 | Currency |  | Y |
| 15 | TX_PUM3 | Text(Wide) | 60 | Y |
| 16 | TX_SU3 | Currency |  | Y |
| 17 | TX_DAN3 | Currency |  | Y |
| 18 | TX_KUM3 | Currency |  | Y |
| 19 | TX_VAT3 | Currency |  | Y |
| 20 | TX_PUM4 | Text(Wide) | 60 | Y |
| 21 | TX_SU4 | Currency |  | Y |
| 22 | TX_DAN4 | Currency |  | Y |
| 23 | TX_KUM4 | Currency |  | Y |
| 24 | TX_VAT4 | Currency |  | Y |
| 25 | TX_GU | Text(Wide) | 1 | Y |
| 26 | TX_GU1 | Text(Wide) | 1 | Y |
| 27 | TX_seq | SmallInt |  | Y |
| 28 | TX_old | Currency |  | Y |
| 29 | TX_y1 | SmallInt |  | Y |
| 30 | TX_y2 | SmallInt |  | Y |
| 31 | TX_SENDDT | Text(Wide) | 8 | Y |
| 32 | TX_READDT | Text(Wide) | 8 | Y |
| 33 | TX_REPORTDT | Text(Wide) | 8 | Y |
| 34 | TX_REM | Text(Wide) | 100 | Y |
| 35 | TX_REM1 | Text(Wide) | 100 | Y |

### DOCF5  (0 rows, 12 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | S_BUY | Long |  | Y |
| 2 | S_YMD | Text(Wide) | 8 | Y |
| 3 | S_SUN | SmallInt |  | Y |
| 4 | S_GU | Text(Wide) | 1 | Y |
| 5 | S_BAL | Currency |  | Y |
| 6 | S_SUK | Currency |  | Y |
| 7 | S_REM | Text(Wide) | 30 | Y |
| 8 | S_SSUN | SmallInt |  | Y |
| 9 | S_SCLA | Text(Wide) | 20 | Y |
| 10 | S_SNO | Text(Wide) | 30 | Y |
| 11 | S_CHERI | Text(Wide) | 6 | Y |
| 12 | S_REM1 | Text(Wide) | 60 | Y |

### DOCF6  (0 rows, 10 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | AC_YMD | Text(Wide) | 8 | Y |
| 2 | AC_JWASU | SmallInt |  | Y |
| 3 | AC_JEN | Text(Wide) | 1 | Y |
| 4 | AC_JEK | Text(Wide) | 30 | Y |
| 5 | AC_AMT | Currency |  | Y |
| 6 | AC_SBUY | Long |  | Y |
| 7 | AC_SYMD | Text(Wide) | 8 | Y |
| 8 | AC_SGU | Text(Wide) | 1 | Y |
| 9 | AC_SSUN | SmallInt |  | Y |
| 10 | AC_cheri | Text(Wide) | 6 | Y |

### DOCF7  (0 rows, 10 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | SC_KCODE | Text(Wide) | 4 | Y |
| 2 | SC_SAWON | Text(Wide) | 30 | Y |
| 3 | SC_DT | Text(Wide) | 8 | Y |
| 4 | SC_SUN | SmallInt |  | Y |
| 5 | SC_JEK | Text(Wide) | 30 | Y |
| 6 | SC_CR | Currency |  | Y |
| 7 | SC_DR | Currency |  | Y |
| 8 | SC_REM | Text(Wide) | 20 | Y |
| 9 | SC_GU | Text(Wide) | 1 | Y |
| 10 | SC_BNO | Text(Wide) | 30 | Y |

### DOCF9  (0 rows, 15 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | EU_CLA | Text(Wide) | 1 | Y |
| 2 | EU_NO | Text(Wide) | 20 | Y |
| 3 | EU_BANK | Text(Wide) | 20 | Y |
| 4 | EU_BAL | Text(Wide) | 20 | Y |
| 5 | EU_ISI | Text(Wide) | 20 | Y |
| 6 | EU_JIK | Text(Wide) | 20 | Y |
| 7 | EU_BDT | Text(Wide) | 8 | Y |
| 8 | EU_MDT | Text(Wide) | 8 | Y |
| 9 | EU_HDT | Text(Wide) | 8 | Y |
| 10 | EU_CDT | Text(Wide) | 8 | Y |
| 11 | EU_GU | Text(Wide) | 1 | Y |
| 12 | EU_AMT | Currency |  | Y |
| 13 | EU_BUY | Text(Wide) | 30 | Y |
| 14 | EU_BUYJ | Text(Wide) | 30 | Y |
| 15 | EU_REM | Text(Wide) | 20 | Y |

### DOCFA  (0 rows, 22 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | IU_NO | Text(Wide) | 10 | Y |
| 2 | IU_SUN | SmallInt |  | Y |
| 3 | IU_PUM | Text(Wide) | 40 | Y |
| 4 | IU_KU | Text(Wide) | 40 | Y |
| 5 | IU_QTY | Currency |  | Y |
| 6 | IU_DAN | Currency |  | Y |
| 7 | IU_AMT | Currency |  | Y |
| 8 | IU_VAT | Currency |  | Y |
| 9 | IU_NAB | Text(Wide) | 8 | Y |
| 10 | IU_REM | Text(Wide) | 30 | Y |
| 11 | IU_JI | Text(Wide) | 20 | Y |
| 12 | IU_JANG | Text(Wide) | 20 | Y |
| 13 | IU_ODT | Text(Wide) | 8 | Y |
| 14 | IU_BUY | Long |  | Y |
| 15 | IU_IDT | Text(Wide) | 8 | Y |
| 16 | IU_IQTY | Currency |  | Y |
| 17 | IU_GU | Text(Wide) | 1 | Y |
| 18 | IU_JIB | Text(Wide) | 1 | Y |
| 19 | IU_YAMT | Currency |  | Y |
| 20 | IU_HSUN | SmallInt |  | Y |
| 21 | IU_HSUN1 | SmallInt |  | Y |
| 22 | IU_DC | Text(Wide) | 15 | Y |

### DOCFB  (0 rows, 18 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | IJ_DT | Text(Wide) | 8 | Y |
| 2 | IJ_IO | Text(Wide) | 1 | Y |
| 3 | IJ_SEQ | SmallInt |  | Y |
| 4 | IJ_BUY | Long |  | Y |
| 5 | IJ_SAWON | Text(Wide) | 30 | Y |
| 6 | IJ_SUN | SmallInt |  | Y |
| 7 | IJ_PUM | Text(Wide) | 40 | Y |
| 8 | IJ_KU | Text(Wide) | 40 | Y |
| 9 | IJ_QTY | Currency |  | Y |
| 10 | IJ_DAN | Currency |  | Y |
| 11 | IJ_AMT | Currency |  | Y |
| 12 | IJ_VAT | Currency |  | Y |
| 13 | IJ_REM | Text(Wide) | 30 | Y |
| 14 | IJ_CHANG | Text(Wide) | 2 | Y |
| 15 | IJ_TAXNO | Text(Wide) | 8 | Y |
| 16 | IJ_DSEQ | Long |  | Y |
| 17 | IJ_TAXBUY | Long |  | Y |
| 18 | IJ_DC | Text(Wide) | 15 | Y |

### DOCFC  (0 rows, 17 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | IM_YM | Text(Wide) | 6 | Y |
| 2 | IM_CHANG | Text(Wide) | 2 | Y |
| 3 | IM_pum | Text(Wide) | 40 | Y |
| 4 | IM_ku | Text(Wide) | 40 | Y |
| 5 | IM_BQTY | Currency |  | Y |
| 6 | IM_BAMT | Currency |  | Y |
| 7 | IM_IQTY | Currency |  | Y |
| 8 | IM_IAMT | Currency |  | Y |
| 9 | IM_OQTY | Currency |  | Y |
| 10 | IM_OAMT | Currency |  | Y |
| 11 | IM_DAN | Currency |  | Y |
| 12 | IM_CQTY | Currency |  | Y |
| 13 | IM_CAMT | Currency |  | Y |
| 14 | IM_QTYS | Currency |  | Y |
| 15 | IM_AMTS | Currency |  | Y |
| 16 | IM_ISQTY | Currency |  | Y |
| 17 | IM_OSQTY | Currency |  | Y |

### DOCFE  (0 rows, 18 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | IJA_DT | Text(Wide) | 8 | Y |
| 2 | IJA_IO | Text(Wide) | 1 | Y |
| 3 | IJA_SEQ | SmallInt |  | Y |
| 4 | IJA_BUY | Long |  | Y |
| 5 | IJA_SAWON | Text(Wide) | 30 | Y |
| 6 | IJA_AMT1 | Currency |  | Y |
| 7 | IJA_AMT2 | Currency |  | Y |
| 8 | IJA_AMT3 | Currency |  | Y |
| 9 | IJA_AMT4 | Currency |  | Y |
| 10 | IJA_AMT5 | Currency |  | Y |
| 11 | IJA_AMT6 | Currency |  | Y |
| 12 | IJA_REM | Text(Wide) | 50 | Y |
| 13 | IJA_LINE | SmallInt |  | Y |
| 14 | IJA_SSUN | SmallInt |  | Y |
| 15 | IJA_SSUNH | SmallInt |  | Y |
| 16 | IJA_SSUNH1 | SmallInt |  | Y |
| 17 | IJA_SSUNC | SmallInt |  | Y |
| 18 | IJA_SSUNE | SmallInt |  | Y |

### DOCFO  (0 rows, 22 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | IO_NO | Text(Wide) | 10 | Y |
| 2 | IO_SUN | SmallInt |  | Y |
| 3 | IO_PUM | Text(Wide) | 40 | Y |
| 4 | IO_KU | Text(Wide) | 40 | Y |
| 5 | IO_QTY | Currency |  | Y |
| 6 | IO_DAN | Currency |  | Y |
| 7 | IO_AMT | Currency |  | Y |
| 8 | IO_VAT | Currency |  | Y |
| 9 | IO_NAB | Text(Wide) | 8 | Y |
| 10 | IO_REM | Text(Wide) | 30 | Y |
| 11 | IO_JI | Text(Wide) | 20 | Y |
| 12 | IO_JANG | Text(Wide) | 20 | Y |
| 13 | IO_ODT | Text(Wide) | 8 | Y |
| 14 | IO_BUY | Long |  | Y |
| 15 | IO_IDT | Text(Wide) | 8 | Y |
| 16 | IO_IQTY | Currency |  | Y |
| 17 | IO_GU | Text(Wide) | 1 | Y |
| 18 | IO_JIB | Text(Wide) | 1 | Y |
| 19 | IO_YAMT | Currency |  | Y |
| 20 | IO_HSUN | SmallInt |  | Y |
| 21 | IO_HSUN1 | SmallInt |  | Y |
| 22 | IO_DC | Text(Wide) | 15 | Y |

### DOCFQ  (0 rows, 10 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | EQ_CLA | Text(Wide) | 1 | Y |
| 2 | EQ_NO | Text(Wide) | 20 | Y |
| 3 | EQ_BANK | Text(Wide) | 20 | Y |
| 4 | EQ_BDT | Text(Wide) | 8 | Y |
| 5 | EQ_MDT | Text(Wide) | 8 | Y |
| 6 | EQ_CDT | Text(Wide) | 8 | Y |
| 7 | EQ_GU | Text(Wide) | 1 | Y |
| 8 | EQ_AMT | Currency |  | Y |
| 9 | EQ_BUYJ | Text(Wide) | 30 | Y |
| 10 | EQ_REM | Text(Wide) | 20 | Y |

### DOCLT  (0 rows, 2 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | LT_MK | Text(Wide) | 20 | Y |
| 2 | LT_TIME | Currency |  | Y |

### REMARK1  (0 rows, 5 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | REM_CODE | Text(Wide) | 11 | Y |
| 2 | REM_1 | Text(Wide) | 100 | Y |
| 3 | REM_2 | Text(Wide) | 100 | Y |
| 4 | REM_3 | Text(Wide) | 100 | Y |
| 5 | REM_4 | Text(Wide) | 100 | Y |

---
## POST.mdb

**테이블 수: **

### 테이블 인덱스

| # | 테이블명 | 행 수 |
|---|---|---:|
| 1 | POS01 | 49,702 |

### POS01  (49,702 rows, 7 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | POS_CODE | Text(Wide) | 6 | Y |
| 2 | POS_AREA1 | Text(Wide) | 30 | Y |
| 3 | POS_AREA2 | Text(Wide) | 30 | Y |
| 4 | POS_AREA3 | Text(Wide) | 50 | Y |
| 5 | POS_AREA4 | Text(Wide) | 50 | Y |
| 6 | POS_AREA5 | Text(Wide) | 50 | Y |
| 7 | POS_KEYAREA | Text(Wide) | 50 | Y |

---
## POTHER.mdb

**테이블 수: 8**

### 테이블 인덱스

| # | 테이블명 | 행 수 |
|---|---|---:|
| 1 | CALENDAR | 7,305 |
| 2 | DELIVERY | 0 |
| 3 | DOCAS | 0 |
| 4 | DOCAS1 | 0 |
| 5 | DOCME | 0 |
| 6 | DOCNM | 0 |
| 7 | DOCSC | 252 |
| 8 | LOCK1 | 0 |

### CALENDAR  (7,305 rows, 12 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | CALENDAR_YMD | Text(Wide) | 8 | Y |
| 2 | CALENDAR_WEEK | SmallInt |  | Y |
| 3 | CALENDAR_DESC | Text(Wide) | 50 | Y |
| 4 | CALENDAR_WORK | SmallInt |  | Y |
| 5 | CALENDAR_ERATE_VND | Currency |  | Y |
| 6 | CALENDAR_ERATE_WON | Currency |  | Y |
| 7 | CALENDAR_REM | Text(Wide) | 50 | Y |
| 8 | CALENDAR_REM1 | Text(Wide) | 50 | Y |
| 9 | CALENDAR_REM2 | Text(Wide) | 50 | Y |
| 10 | CALENDAR_REM3 | Text(Wide) | 50 | Y |
| 11 | CALENDAR_REM4 | Text(Wide) | 50 | Y |
| 12 | CALENDAR_REM5 | Text(Wide) | 50 | Y |

### DELIVERY  (0 rows, 15 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | DEL_DATE | Text(Wide) | 8 | Y |
| 2 | DEL_TIME | Text(Wide) | 6 | Y |
| 3 | DEL_BUY | Text(Wide) | 50 | Y |
| 4 | DEL_DEST | Text(Wide) | 50 | Y |
| 5 | DEL_SUN | SmallInt |  | Y |
| 6 | DEL_PUM | Text(Wide) | 50 | Y |
| 7 | DEL_TEL | Text(Wide) | 30 | Y |
| 8 | DEL_PACK | Text(Wide) | 10 | Y |
| 9 | DEL_QTY | Currency |  | Y |
| 10 | DEL_COST | Text(Wide) | 10 | Y |
| 11 | DEL_COST1 | Currency |  | Y |
| 12 | DEL_TB | Text(Wide) | 1 | Y |
| 13 | DEL_DELSERVICE | Text(Wide) | 50 | Y |
| 14 | DEL_REM1 | Text(Wide) | 100 | Y |
| 15 | DEL_REM2 | Text(Wide) | 50 | Y |

### DOCAS  (0 rows, 17 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | AS_DT | Text(Wide) | 8 | Y |
| 2 | AS_TM | Text(Wide) | 4 | Y |
| 3 | AS_BUY | Long |  | Y |
| 4 | AS_GU | Text(Wide) | 1 | Y |
| 5 | AS_YO1 | Text(Wide) | 60 | Y |
| 6 | AS_YO2 | Text(Wide) | 20 | Y |
| 7 | AS_YODT | Text(Wide) | 8 | Y |
| 8 | AS_YOTM | Text(Wide) | 10 | Y |
| 9 | AS_CHDT | Text(Wide) | 8 | Y |
| 10 | AS_CH1 | Text(Wide) | 60 | Y |
| 11 | AS_CH2 | Text(Wide) | 20 | Y |
| 12 | AS_CHDAM | Text(Wide) | 30 | Y |
| 13 | AS_JEBDAM | Text(Wide) | 30 | Y |
| 14 | AS_COST | Currency |  | Y |
| 15 | AS_HANGGU | Text(Wide) | 20 | Y |
| 16 | AS_KKK | Text(Wide) | 1 | Y |
| 17 | AS_ACCOUNT | SmallInt |  | Y |

### DOCAS1  (0 rows, 14 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | AS1_BUY | Long |  | Y |
| 2 | AS1_GU | Text(Wide) | 1 | Y |
| 3 | AS1_NO | Text(Wide) | 20 | Y |
| 4 | AS1_PASS | Text(Wide) | 8 | Y |
| 5 | AS1_DT | Text(Wide) | 2 | Y |
| 6 | AS1_AMT | Long |  | Y |
| 7 | AS1_JIBUL | Text(Wide) | 1 | Y |
| 8 | AS1_SDT | Text(Wide) | 8 | Y |
| 9 | AS1_EDT | Text(Wide) | 8 | Y |
| 10 | AS1_IBKUM | Text(Wide) | 14 | Y |
| 11 | AS1_IBKUMdt | Text(Wide) | 96 | Y |
| 12 | AS1_DAM | Text(Wide) | 30 | Y |
| 13 | AS1_area | Text(Wide) | 6 | Y |
| 14 | AS1_REM | Text(Wide) | 20 | Y |

### DOCME  (0 rows, 10 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | ME_DATE | Text(Wide) | 8 | Y |
| 2 | ME_TIME | Text(Wide) | 6 | Y |
| 3 | ME_GUBUN | Text(Wide) | 10 | Y |
| 4 | ME_SAWON | Text(Wide) | 30 | Y |
| 5 | ME_DESC1 | Text(Wide) | 40 | Y |
| 6 | ME_DESC2 | Text(Wide) | 40 | Y |
| 7 | ME_DESC3 | Text(Wide) | 40 | Y |
| 8 | ME_DESC4 | Text(Wide) | 40 | Y |
| 9 | ME_DESC5 | Text(Wide) | 40 | Y |
| 10 | ME_NOTICE | SmallInt |  | Y |

### DOCNM  (0 rows, 34 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | NAM_OWNER | Text(Wide) | 30 | Y |
| 2 | NAM_NAME | Text(Wide) | 30 | Y |
| 3 | nam_JUMIN | Text(Wide) | 14 | Y |
| 4 | NAM_COM | Text(Wide) | 50 | Y |
| 5 | nam_postno | Text(Wide) | 7 | Y |
| 6 | nam_addr | Text(Wide) | 50 | Y |
| 7 | nam_buse | Text(Wide) | 20 | Y |
| 8 | nam_jik | Text(Wide) | 20 | Y |
| 9 | nam_tel | Text(Wide) | 18 | Y |
| 10 | nam_fax | Text(Wide) | 18 | Y |
| 11 | nam_HPAGE | Text(Wide) | 30 | Y |
| 12 | nam_hp | Text(Wide) | 18 | Y |
| 13 | nam_pp | Text(Wide) | 18 | Y |
| 14 | nam_HPOSTNO | Text(Wide) | 7 | Y |
| 15 | nam_HADDR | Text(Wide) | 50 | Y |
| 16 | nam_HTEL | Text(Wide) | 18 | Y |
| 17 | NAM_EMAIL | Text(Wide) | 50 | Y |
| 18 | nam_birth | Text(Wide) | 10 | Y |
| 19 | nam_YE | Byte |  | Y |
| 20 | nam_GU | Byte |  | Y |
| 21 | nam_MARRY | Byte |  | Y |
| 22 | nam_RDATE | Text(Wide) | 8 | Y |
| 23 | nam_OPEN | SmallInt |  | Y |
| 24 | nam_PICTURE | Text(Wide) | 30 | Y |
| 25 | nam_keytel | Text(Wide) | 18 | Y |
| 26 | nam_ccode | Text(Wide) | 10 | Y |
| 27 | nam_rem1 | Text(Wide) | 40 | Y |
| 28 | nam_rem2 | Text(Wide) | 40 | Y |
| 29 | nam_rem3 | Text(Wide) | 40 | Y |
| 30 | nam_rem4 | Text(Wide) | 40 | Y |
| 31 | nam_rem5 | Text(Wide) | 40 | Y |
| 32 | nam_rem6 | Text(Wide) | 40 | Y |
| 33 | nam_rem7 | Text(Wide) | 40 | Y |
| 34 | nam_rem8 | Text(Wide) | 40 | Y |

### DOCSC  (252 rows, 8 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | SC_DATE | Text(Wide) | 8 | Y |
| 2 | SC_TIME | Text(Wide) | 6 | Y |
| 3 | SC_SAWON | Text(Wide) | 30 | Y |
| 4 | SC_DESC1 | Text(Wide) | 60 | Y |
| 5 | SC_DESC2 | Text(Wide) | 60 | Y |
| 6 | SC_DESC3 | Text(Wide) | 60 | Y |
| 7 | SC_DESC4 | Text(Wide) | 2 | Y |
| 8 | SC_OPEN | SmallInt |  | Y |

### LOCK1  (0 rows, 5 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | LOCK_CODE | Text(Wide) | 30 | Y |
| 2 | LOCK_DT | Text(Wide) | 8 | Y |
| 3 | LOCK_TM | Text(Wide) | 4 | Y |
| 4 | LOCK_SW | Text(Wide) | 30 | Y |
| 5 | LOCK_DESC | Text(Wide) | 50 | Y |

---
## PYOJUN.MDB

**테이블 수: 6**

### 테이블 인덱스

| # | 테이블명 | 행 수 |
|---|---|---:|
| 1 | COSTNO | 33 |
| 2 | DOCF8 | 3 |
| 3 | DOCFS | 0 |
| 4 | DOCRT | 0 |
| 5 | DOCSW | 0 |
| 6 | SETUP | 172 |

### COSTNO  (33 rows, 3 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | CT_CODE | Text(Wide) | 4 | Y |
| 2 | CT_DESC | Text(Wide) | 20 | Y |
| 3 | CT_REM | Text(Wide) | 10 | Y |

### DOCF8  (3 rows, 41 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | buy_code | Long |  | N |
| 2 | buy_name | Text(Wide) | 50 | Y |
| 3 | buy_tel | Text(Wide) | 18 | Y |
| 4 | buy_tel1 | Text(Wide) | 18 | Y |
| 5 | buy_fax | Text(Wide) | 18 | Y |
| 6 | buy_top | Text(Wide) | 30 | Y |
| 7 | buy_topjumin | Text(Wide) | 14 | Y |
| 8 | buy_postno | Text(Wide) | 7 | Y |
| 9 | buy_addr | Text(Wide) | 100 | Y |
| 10 | buy_addr1 | Text(Wide) | 60 | Y |
| 11 | buy_taxno | Text(Wide) | 12 | Y |
| 12 | buy_euptae | Text(Wide) | 20 | Y |
| 13 | buy_eupjong | Text(Wide) | 30 | Y |
| 14 | buy_damdangbu | Text(Wide) | 20 | Y |
| 15 | buy_damdang | Text(Wide) | 30 | Y |
| 16 | buy_damdang1 | Text(Wide) | 30 | Y |
| 17 | buy_ccode | Text(Wide) | 30 | Y |
| 18 | buy_mayul | Currency |  | Y |
| 19 | buy_halyul | Currency |  | Y |
| 20 | buy_cardyul | Currency |  | Y |
| 21 | buy_yeasin | Currency |  | Y |
| 22 | buy_taxgubun | Text(Wide) | 6 | Y |
| 23 | buy_taxdt | Text(Wide) | 10 | Y |
| 24 | buy_startdt | Text(Wide) | 10 | Y |
| 25 | buy_bank | Text(Wide) | 40 | Y |
| 26 | buy_bankno | Text(Wide) | 20 | Y |
| 27 | buy_bankname | Text(Wide) | 20 | Y |
| 28 | buy_sawon | Text(Wide) | 20 | Y |
| 29 | buy_rem | Text(Wide) | 60 | Y |
| 30 | buy_rem1 | Text(Wide) | 60 | Y |
| 31 | buy_rem2 | Text(Wide) | 60 | Y |
| 32 | buy_rem3 | Text(Wide) | 60 | Y |
| 33 | buy_rem4 | Text(Wide) | 60 | Y |
| 34 | buy_rem5 | Text(Wide) | 60 | Y |
| 35 | buy_rem6 | Text(Wide) | 60 | Y |
| 36 | buy_gu | Text(Wide) | 4 | Y |
| 37 | buy_keyname | Text(Wide) | 50 | Y |
| 38 | buy_keytel | Text(Wide) | 18 | Y |
| 39 | buy_keybirth | Text(Wide) | 4 | Y |
| 40 | buy_DOSCODE | Text(Wide) | 5 | Y |
| 41 | BUY_FIL | Text(Wide) | 30 | Y |

### DOCFS  (0 rows, 21 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | S_PUM | Text(Wide) | 40 | Y |
| 2 | S_KU | Text(Wide) | 40 | Y |
| 3 | S_DANW | Text(Wide) | 4 | Y |
| 4 | S_DESC | Text(Wide) | 40 | Y |
| 5 | S_MAKER | Text(Wide) | 20 | Y |
| 6 | S_IBUY | Text(Wide) | 50 | Y |
| 7 | S_JEK | Currency |  | Y |
| 8 | S_IDAN | Currency |  | Y |
| 9 | S_IDANA | Currency |  | Y |
| 10 | S_IDANB | Currency |  | Y |
| 11 | S_PDAN | Currency |  | Y |
| 12 | S_PDANA | Currency |  | Y |
| 13 | S_PDANB | Currency |  | Y |
| 14 | S_PDANC | Currency |  | Y |
| 15 | S_PDAND | Currency |  | Y |
| 16 | S_PDANE | Currency |  | Y |
| 17 | S_SET | Text(Wide) | 1 | Y |
| 18 | S_TAX | Text(Wide) | 1 | Y |
| 19 | S_CCODE | Text(Wide) | 20 | Y |
| 20 | S_BARCODE | Text(Wide) | 20 | Y |
| 21 | S_FIL | Text(Wide) | 30 | Y |

### DOCRT  (0 rows, 10 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | RT_PUM | Text(Wide) | 40 | Y |
| 2 | RT_KU | Text(Wide) | 40 | Y |
| 3 | RT_SUN | SmallInt |  | Y |
| 4 | RT_RPUM | Text(Wide) | 40 | Y |
| 5 | RT_RKU | Text(Wide) | 40 | Y |
| 6 | RT_UNIT | Currency |  | Y |
| 7 | RT_ABS | Currency |  | Y |
| 8 | RT_GU | Text(Wide) | 1 | Y |
| 9 | RT_SON | Currency |  | Y |
| 10 | RT_KUM | Currency |  | Y |

### DOCSW  (0 rows, 36 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | SW_NAME | Text(Wide) | 30 | Y |
| 2 | SW_BUSEA | Text(Wide) | 30 | Y |
| 3 | SW_JIKKUB | Text(Wide) | 20 | Y |
| 4 | SW_JIKCHAK | Text(Wide) | 30 | Y |
| 5 | SW_IBSAIL | Text(Wide) | 10 | Y |
| 6 | SW_POSTNO | Text(Wide) | 7 | Y |
| 7 | SW_ADDR | Text(Wide) | 80 | Y |
| 8 | SW_TEL | Text(Wide) | 18 | Y |
| 9 | SW_HP | Text(Wide) | 18 | Y |
| 10 | SW_BB | Text(Wide) | 60 | Y |
| 11 | SW_PAY | Long |  | Y |
| 12 | SW_JUMIN | Text(Wide) | 14 | Y |
| 13 | SW_BIRTH | Text(Wide) | 10 | Y |
| 14 | SW_HONIN | Text(Wide) | 1 | Y |
| 15 | SW_REM | Text(Wide) | 60 | Y |
| 16 | SW_nation | Text(Wide) | 20 | Y |
| 17 | SW_BIRTHgu | Byte |  | Y |
| 18 | SW_BIRTHtel | Byte |  | Y |
| 19 | SW_PAYgu | Byte |  | Y |
| 20 | SW_PAYeuy | Byte |  | Y |
| 21 | SW_PAYkuk | Byte |  | Y |
| 22 | SW_PAYoth | Text(Wide) | 100 | Y |
| 23 | SW_TELem | Text(Wide) | 20 | Y |
| 24 | SW_TEA | Byte |  | Y |
| 25 | SW_TEADT | Text(Wide) | 8 | Y |
| 26 | SW_TEARESON | Text(Wide) | 50 | Y |
| 27 | SW_BAL1 | Text(Wide) | 120 | Y |
| 28 | SW_BAL2 | Text(Wide) | 120 | Y |
| 29 | SW_BAL3 | Text(Wide) | 120 | Y |
| 30 | SW_BAL4 | Text(Wide) | 120 | Y |
| 31 | SW_BAL5 | Text(Wide) | 120 | Y |
| 32 | SW_BAL6 | Text(Wide) | 120 | Y |
| 33 | SW_BAL7 | Text(Wide) | 120 | Y |
| 34 | SW_BAL8 | Text(Wide) | 120 | Y |
| 35 | SW_BAL9 | Text(Wide) | 120 | Y |
| 36 | SW_BAL10 | Text(Wide) | 120 | Y |

### SETUP  (172 rows, 2 cols)

| 순 | 컬럼 | 타입 | 길이 | NULL |
|---:|---|---|---:|:---:|
| 1 | SET_CODE | SmallInt |  | Y |
| 2 | SET_DESC | Text(Wide) | 100 | Y |

