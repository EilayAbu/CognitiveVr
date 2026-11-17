# אירועי לוג חדשים ומעודכנים

כל השורות נכתבות ל-`*_events.csv` (קטגוריה `task`), חוץ מ-`gaze_merged` שנכתבת ל-`*_gaze.csv`.
`details` הוא רשימת `key=value` מופרדת ב-`|`. זמנים: `t_logger_s` מתחילת הלוגר, `t_session_s` מלחיצת Start.

## גייז – איחוד מבטים (`GazeObjectTracker`)
| event | object | value | details |
|---|---|---|---|
| `gaze_merged` | האובייקט | משך כולל מתחילת המבט הראשון עד סוף האחרון (כולל ההצצות הצידה) | `on_target_s` זמן בפועל על האובייקט · `away_s` זמן ההצצות · `segments` כמה מבטים אוחדו · `start_at_s` (t_logger) · `interrupted_by` על מה הציץ בינתיים (`;`) |

- מבטים על אותו אובייקט שהפער ביניהם ≤ `Merge Gap Seconds` (3 שנ') מאוחדים. השורות הגולמיות `gaze_enter`/`gaze_exit` נשארות.
- נכתבת רק כשאוחדו 2+ מבטים.
- ב-`dwell_total` נוסף `merged_looks=`, וב-JSON (`items[]`) נוסף `mergedLookCount`.

## טלפון – קריאת הודעה (`PhoneSessionEventBridge`)
| event | object | value | details |
|---|---|---|---|
| `phone_msg_read` | BossSmsMessage / WeatherAlertMessage | שניות גייז על הטלפון בזמן שההודעה פתוחה | `threshold_s` · `since_open_s` · `since_arrival_s` · `t_session_s` |
| `phone_msg_closed` (עודכן) | | | נוסף `gaze_while_open_s` (בפתיחה הזו) · `read=0/1` |
| `phone_msg_never_read` | | סה"כ שניות גייז בזמן פתיחה | `opened=0/1` · `gaze_while_open_s` · `threshold_s` (נכתבת בסוף הסשן) |

- **מתי נחשב "נקרא":** כשה-gaze tracker מסתכל על `Phone` / `Phone Gaze` (שדה `Phone Gaze Names`) בזמן שההודעה פתוחה, במשך `Read Threshold Seconds` (1.5 שנ') מצטבר.
- **ב-JSON:** בכל הודעה נוספו `gazeWhileOpenSeconds`, `read`, `readAt`, `readAtSession`.

## מטריה מול התראת הגשם (`PhoneSessionEventBridge`)
| event | object | value | details |
|---|---|---|---|
| `umbrella_at_rain_alert` | umbrella | 1 = כבר בתיק כשההתראה הגיעה, 0 = לא | `in_bag` · `rain_alert_t_session_s` |
| `umbrella_bagged_after_rain` | umbrella | שניות מההתראה | `since_rain_alert_s` · `was_in_bag_at_alert` |

- **ב-JSON** (`phoneMessages`): `rainAlertAtSession`, `umbrellaInBagBeforeRain` (1/0/-1), `umbrellaBaggedAfterRainSeconds`.

## קופסת טוסט בתיק (`LunchBoxDataBridge`)
| event | object | value | details |
|---|---|---|---|
| `lunchbox_in_bag` | LunchBox | 1 = עם טוסט, 0 = ריקה | `has_toast` · `toast_state` (מצב הטוסטר כשהטוסט נסגר בקופסה: Ready/Overcooked/Burnt) · `cook_s` · `in_count` |
| `lunchbox_out_of_bag` | LunchBox | | `has_toast` |
| `box_sealed` (עודכן) | | | נוסף `toast_state` · `cook_s` |

## טוסטר – סדר הפעלה (`ToasterDataBridge`)
| event | value | details |
|---|---|---|
| `toaster_power` (עודכן) | | נוסף `toast_inside=0/1` · `state` |
| `toast_inserted` / `toast_removed` (עודכן) | | נוסף `power_on=0/1` |
| `toaster_cooking_started` | | `order=power_first` (הטוסטר דלק ריק לפני שהטוסט נכנס) / `toast_first` · `empty_run_s` כמה זמן דלק ריק · `since_power_on_s` · `since_toast_in_s` |
| `toaster_state` במעבר ל-Ready (עודכן) | | נוסף `cook_s` (צריך להיות ≈60 = Cook Time) · `wall_s` (ההפרש = זמן שהבישול הושהה) |

- **ב-JSON** (`toasterTask`): `cookingOrder`, `emptyRunSeconds`.
- **התנהגות:** הבישול סופר רק כשהטוסט בפנים, המכסה סגור והחשמל דלוק. 60 השניות נספרות מרגע שכל השלושה מתקיימים, ולא מרגע הדלקת הטוסטר.

## גשם / חלון (`WindowPuddleTaskBridge`)
- **שדה חדש `since_rain_alert_s`:** נוסף ל-`rain_task_start`, `rain_task_window_opened`, `rain_task_first_action`, `rain_task_window_closed`, `rain_task_puddle_cleaned`, `rain_task_complete`.
- **ב-`rain_task_start`:** הערך צריך להיות ≈30, כי זה ה-OpenDelay של החלון אחרי ההתראה.
- **ב-JSON** (`windowPuddleTask`): `rainAlertAt`, `taskStartSinceRainAlert`, `windowOpenedSinceRainAlert`, `windowClosedSinceRainAlert`.

## משימת המפתח (`KeyTaskBridge`)
| event | value | details |
|---|---|---|
| `key_first_seen` (עודכן) | t_logger | `distance_m` · `hint_delay_s` – **ספירת הרמזים מתחילה כאן** |
| `key_hint_started` (היה `key_hint_glow_on`) | t_logger | `hint=glow` · `since_first_seen_s` |
| `key_hint_chair_nudge` (עודכן) | מספר נדנוד | `nudge` · `since_hint_start_s` |
| `key_hint_chair_nudge_skipped` | נדנודים עד כה | נדנוד מתוזמן שלא קרה (הכיסא מוחזק או זז) · `skipped` · `since_hint_start_s` |
| `key_reach_attempt` (עודכן) | מספר ניסיון | `obj=` מה נכנס לאזור (יד או כל אובייקט פיזיקלי: מטריה, מגב, כיסא…; היה `hand=`) · `attempt` · `in_stool_zone` |
| `key_task_solved` (נוקה) | t_logger | ראו למטה |

שדות `key_task_solved`:
- **`method`:** איך נפתרה המשימה: `hand` (דרך השרפרף) / `umbrella` / `mop`.
- **`solve_s`:** זמן הפתרון מתחילת הסשן (t_session).
- **`since_first_seen_s`:** כמה זמן עבר מהמבט הראשון על המפתח ועד הפתרון.
- **`first_attempt`:** מה ניסה קודם: `stool` / `reach` / `umbrella` / `mop`.
- **`reach_attempts`:** כמה פעמים משהו נכנס לאזור סביב המפתח.
- **`stool_visits`:** כמה פעמים נכנס לאזור העמידה ליד השרפרף (רק אחרי Start).
- **`slow_hits`:** מכות בכלי שהיו איטיות מדי להפיל את המפתח.
- **הוסר:** `tool` (מעולם לא התמלא).

**הוסרו השורות לכל ביקור באזור השרפרף:** `key_stool_zone_enter`, `key_stool_zone_exit`, `key_stool_standable`. נשארת רק הספירה: `stool_visits` למעלה, וב-JSON `stoolZoneEnterCount`, `totalStoolZoneSeconds`, `stoolStandableCount`. ביקורים לפני לחיצת Start (בזמן ההוראות) לא נספרים, ולכן גם לא נרשמים כ-`first_attempt=stool`.

אזור המבט במפתח הורחב ל-20° כניסה / 26° יציאה (`chair&keymission.prefab`, `DelayedGazeGlow`).

## יציאה (`SessionEndTrigger`)
אפשר לצאת בכל זמן, עם תיק או בלי. ההחלטה לפי מיקום **הראש** ביחס לקופסת הדלת (EndSessionTrigger), והגובה לא נבדק.

| event | value | details |
|---|---|---|
| `exit_pre_zone_enter` | מספר ביקור | נכנס לאזור הדלת (לפני היציאה האמיתית) · `visit` · `backpack_taken` · `backpack_dist_m` · `items` |
| `exit_pre_zone_return` | שניות באזור | חזר פנימה לבית ("נזכר במשהו") · `visit` · `time_in_zone_s` · `returns` |
| `exit_complete` | פריטים בתיק | `backpack_taken=0/1` (התיק עד 1.5 מ' מהראש) · `backpack_dist_m` · `items` · `item_list` · `pre_zone_visits` · `pre_zone_returns` · `time_in_pre_zone_s` |
| `exit_timeout` | פריטים בתיק | הזמן נגמר (540 שנ'). אותם שדות + `in_pre_zone` |
| `planned_end_reached` (קטגוריה `session`) | שניות מ-Start | סימון בלבד: עברו 420 שנ' (הסיום המתוכנן). הסשן והלוג ממשיכים עד 540 · `planned_end_s` · `auto_end_at_s`. לא נכתבת אם המשתתף כבר יצא |

- **הוסר:** `exit_denied`. אין יותר תנאי של 5 פריטים.
- **ב-JSON:** בלוק `exit` חדש, עם `how`, `backpackTaken`, `backpackDistanceMeters`, `itemsInBackpack`, `preZoneVisits`, `preZoneReturns`, `atSessionSeconds`.
- **`endReason`:** `exit_player` (היה `exit_with_backpack`) / `simulation_timeout` / `manual`.
- **משך הלוג:** `SessionDuration` ב-MainScene שונה מ-420 ל-545. הלוג נשאר פתוח עד הסיום ב-540, ו-545 הוא רק גיבוי.
